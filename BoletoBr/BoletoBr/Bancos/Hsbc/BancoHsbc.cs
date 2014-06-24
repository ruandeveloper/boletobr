﻿using System;
using System.Collections.Generic;
using System.Security.AccessControl;
using BoletoBr.CalculoModulo;
using BoletoBr.Bancos;

namespace BoletoBr.Bancos.Hsbc
{
    public class BancoHsbc : IBanco
    {
        public string CodigoBanco { get; set; }
        public string DigitoBanco { get; set; }
        public string NomeBanco { get; set; }
        public BancoHsbc()
        {
            CodigoBanco = "399";
            DigitoBanco = "9";
            NomeBanco = "HSBC";

            /* Adiciona carteiras de cobrança */
            _carteirasCobrancaHsbc = new List<CarteiraCobranca>();
            _carteirasCobrancaHsbc.Add(new CarteiraCobrancaHsbcCnr());
            _carteirasCobrancaHsbc.Add(new CarteiraCobrancaHsbcCsb());
        }
        
        private int _digitoAutoConferenciaCodigoBarras;
        private string _digitoAutoConferenciaNossoNumero;
        private readonly List<CarteiraCobranca> _carteirasCobrancaHsbc;
        public List<CarteiraCobranca> GetCarteirasCobranca()
        {
            return _carteirasCobrancaHsbc;
        }

        public CarteiraCobranca GetCarteiraCobrancaPorCodigo(string codigoCarteira)
        {
            return GetCarteirasCobranca().Find(fd => fd.Codigo == codigoCarteira);
        }

        /// <summary>
        /// Valida se o boleto está preenchido com os campos mínimos requeridos.
        /// Dispara uma ApplicationException caso esteja faltando alguma informação.
        /// </summary>
        public void ValidaBoletoComNormasBanco(Boleto boleto)
        {
            //Verifica as carteiras implementadas
            if (!boleto.CarteiraCobranca.Codigo.Equals("CSB") &
                !boleto.CarteiraCobranca.Codigo.Equals("CNR"))
                throw new NotImplementedException("Carteira n�o implementada. Utilize a carteira 'CSB' ou 'CNR'.");

            //Verifica se o nosso n�mero � v�lido
            if (boleto.NossoNumeroFormatado.ToStringSafe() == string.Empty)
                throw new NotImplementedException("Nosso número inválido");

            //Verifica se o nosso n�mero � v�lido
            if (boleto.NossoNumeroFormatado.ToStringSafe().ToLong() == 0)
                throw new NotImplementedException("Nosso número inválido");

            //Verifica se o tamanho para o NossoNumero s�o 10 d�gitos (5 range + 5 numero sequencial)
            if (Convert.ToInt32(boleto.NossoNumeroFormatado).ToString().Length > 10)
                throw new NotImplementedException("A quantidade de dígitos do nosso número para a carteira " + boleto.CarteiraCobranca.Codigo + ", são 10 números.");
            else if (Convert.ToInt32(boleto.NossoNumeroFormatado).ToString().Length < 10)
                boleto.SetNossoNumeroFormatado(boleto.NossoNumeroFormatado.PadLeft(10, '0'));
        }

        public void FormatarBoleto(Boleto boleto)
        {
            boleto.ValidaDadosEssenciaisDoBoleto();

            ValidaBoletoComNormasBanco(boleto);

            // Calcula o DAC do Nosso N�mero
            // Nosso N�mero = Range(5) + Numero Sequencial(5)
            _digitoAutoConferenciaNossoNumero = Common.Mod11(boleto.NossoNumeroFormatado, 7).ToString();
            
            //Atribui o nome do banco ao local de pagamento
            boleto.LocalPagamento = "PAGAR PREFERENCIALMENTE EM AGÊNCIAS DO HSBC";

            FormataNumeroDocumento(boleto);
            FormataNossoNumero(boleto);
            FormataLinhaDigitavel(boleto);
            FormataCodigoBarra(boleto);
        }

        public void FormataNumeroDocumento(Boleto boleto)
        {
            boleto.NumeroDocumento = boleto.NumeroDocumento.PadLeft(7, '0');
        }

        public void FormataNossoNumero(Boleto boleto)
        {
            try
            {
                if (boleto.CarteiraCobranca.Codigo == "CSB")
                {
                    string nossoNumeroComposto =
                    boleto.CedenteBoleto.CodigoCedente.PadLeft(5, '0')
                    +
                    boleto.SequencialNossoNumero.PadLeft(5, '0');

                    string digitoAutoConferenciaNossoNumero = Common.Mod11(nossoNumeroComposto, 7).ToString();

                    string nossoNumeroFormatado =
                        nossoNumeroComposto + digitoAutoConferenciaNossoNumero;

                    boleto.SetNossoNumeroFormatado(nossoNumeroFormatado);
                    return;
                }
                if (boleto.CarteiraCobranca.Codigo == "CNR")
                {
                    /* Seguindo documentação CNR - Cobrança Não Registrada
                     * Disponível em: https://www.hsbc.com.br/1/PA_esf-ca-app-content/content/hbbr-pws-gip16/portugues/business/comum/pdf/cnrbarra.pdf
                     */

                    string codigoDoPagador = boleto.SequencialNossoNumero;
                    string primeiroDigitoVerificador = CalculaPrimeiroDigitoVerificadorCnrTipo4(boleto.SequencialNossoNumero);
                    string segundoDigitoVerificador =
                       CalculaSegundoDigitoVerificadorCnrTipo4(boleto.SequencialNossoNumero,
                       primeiroDigitoVerificador, boleto.CedenteBoleto.CodigoCedente,
                       boleto.DataVencimento);

                    boleto.SetNossoNumeroFormatado(
                        String.Format("{0}{1}4{2}",
                            codigoDoPagador,
                            primeiroDigitoVerificador,
                            segundoDigitoVerificador));

                    /* Padroniza com 16 dígitos */
                    boleto.SetNossoNumeroFormatado(
                        boleto.NossoNumeroFormatado.PadLeft(16, '0'));
                    return;
                }

                throw new NotImplementedException("Modelo de carteira de cobrança: " + boleto.CarteiraCobranca.Codigo +
                                                  " não está implementado.");
            }
            catch (Exception ex)
            {
                throw new Exception("Falha ao tentar formatar nosso número.", ex);
            }
        }

        public void FormataLinhaDigitavel(Boleto boleto)
        {
            string nossoNumeroLinhaDigitavel = boleto.NossoNumeroFormatado.PadLeft(13, '0');
            string codigoCedente = boleto.CedenteBoleto.CodigoCedente.PadLeft(7, '0');
            string digitoAutoConferenciaNossoNumero = Common.Mod11(boleto.NossoNumeroFormatado, 7).ToString();

            string C1 = string.Empty;
            string C2 = string.Empty;
            string C3 = string.Empty;
            string C5 = string.Empty;

            string AAA;
            string B;
            string CCCCC;
            string X;


            string DDDDDD; 
            string DD;
            string EEEE; 
            string EEEEEEEE;
            string Y;
            string FFFFFFF; 
            string FFFFF;
            string GGGGG;
            string Z;

            if (boleto.CarteiraCobranca.Codigo == "CSB")
            {
                #region AAABC.CCCCX

                AAA = this.CodigoBanco.PadLeft(3, '0');
                B = boleto.Moeda.ToString();
                CCCCC = boleto.NossoNumeroFormatado.Substring(0, 5);
                X = Common.Mod10(AAA + B + CCCCC).ToString();

                C1 = String.Format("{0}{1}{2}.", AAA, B, CCCCC.Substring(0, 1));
                C1 += String.Format("{0}{1} ", CCCCC.Substring(1, 4), X);
                #endregion

                #region DDDDD.DEEEEY

                DDDDDD = boleto.NossoNumeroFormatado.Substring(5, 5) + digitoAutoConferenciaNossoNumero;
                EEEE = boleto.CedenteBoleto.ContaBancariaCedente.Agencia.PadLeft(4, '0');
                Y = Common.Mod10(DDDDDD + EEEE).ToString();

                C2 = String.Format("{0}.", DDDDDD.Substring(0, 5));
                C2 += string.Format("{0}{1}{2} ", DDDDDD.Substring(5, 1), EEEE, Y);

                #endregion

                #region FFFFF.FF001Z

                FFFFFFF = boleto.CedenteBoleto.ContaBancariaCedente.Conta.PadLeft(7, '0');
                Z = Common.Mod10(FFFFFFF + "001").ToString();

                C3 = String.Format("{0}.", FFFFFFF.Substring(0, 5));
                C3 += String.Format("{0}001{1}", FFFFFFF.Substring(5, 2), Z);

                #endregion
            }
            if (boleto.CarteiraCobranca.Codigo == "CNR")
            {
                #region AAABC.CCCCX

                AAA = this.CodigoBanco.PadLeft(3, '0');
                B = boleto.Moeda.ToString();
                CCCCC = boleto.CedenteBoleto.CodigoCedente.Substring(0, 5);
                X = Common.Mod10(AAA + B + CCCCC).ToString();

                C1 = string.Format("{0}{1}{2}.", AAA, B, CCCCC.Substring(0, 1));
                C1 += string.Format("{0}{1} ", CCCCC.Substring(1, 4), X);

                #endregion AAABC.CCDDX

                #region DDEEE.EEEEEY

                DD = boleto.CedenteBoleto.CodigoCedente.Substring(5, 2);
                EEEEEEEE = nossoNumeroLinhaDigitavel.Substring(0, 8);
                Y = Common.Mod10(DD + EEEEEEEE).ToString();

                C2 = string.Format("{0}{1}.", DD, EEEEEEEE.Substring(0, 3));
                C2 += string.Format("{0}{1} ", EEEEEEEE.Substring(3, 5), Y);

                #endregion DDEEE.EEEEEY

                #region FFFFF.GGGGGZ

                FFFFF = nossoNumeroLinhaDigitavel.Substring(8, 5);
                GGGGG = (boleto.DataVencimento.DayOfYear + boleto.DataVencimento.ToString("yy").Substring(1, 1)).PadLeft(4, '0') + "2";

                Z = Common.Mod10(FFFFF + GGGGG).ToString();

                C3 = string.Format("{0}.", FFFFF);
                C3 += string.Format("{0}{1}", GGGGG, Z);

                #endregion FFFFF.GGGGGZ
            }

            string W = String.Format(" {0} ", _digitoAutoConferenciaCodigoBarras);

            #region HHHHIIIIIIIIII

            string HHHH = Common.FatorVencimento(boleto.DataVencimento).ToString();
            string IIIIIIIIII = boleto.ValorBoleto.ToString("f").Replace(",", "").Replace(".", "");

            IIIIIIIIII = IIIIIIIIII.PadLeft(10, '0');
                C5 = HHHH + IIIIIIIIII;

                #endregion HHHHHHHHHHHHHH

            boleto.LinhaDigitavelBoleto = C1+C2+C3+W+C5;
        }

        public void FormataCodigoBarra(Boleto boleto)
        {
            try
            {
                /* Preenche com 0´s a esquerda
                 * 10 caracteres
                 */
                string valorBoletoTexto =
                    boleto.ValorBoleto.ToString("f")
                    .Replace(",", "")
                    .Replace(".", "")
                    .PadLeft(10, '0');

                string numeroDocumentoFormatado =
                    boleto.NumeroDocumento.PadLeft(7, '0');

                string codigoBarraSemDigitoVerificador = null;

                if (boleto.CarteiraCobranca.Codigo == "CSB")
                {
                    codigoBarraSemDigitoVerificador =
                        String.Format("{0}{1}{2}{3}{4}{5}{6}001",
                            this.CodigoBanco,
                            boleto.Moeda,
                        //9999 --> 21/02/2025
                            Common.FatorVencimento(boleto.DataVencimento),
                            valorBoletoTexto,
                            boleto.NossoNumeroFormatado + boleto.DigitoNossoNumero,
                            boleto.CedenteBoleto.ContaBancariaCedente.Agencia.PadLeft(4, '0'),
                            boleto.CedenteBoleto.ContaBancariaCedente.Conta.PadLeft(7, '0')
                            );
                }
                if (boleto.CarteiraCobranca.Codigo == "CNR")
                {
                    codigoBarraSemDigitoVerificador =
                        String.Format("{0}{1}{2}{3}{4}{5}{6}2",
                            this.CodigoBanco,
                            boleto.Moeda,
                        //9999 --> 21/02/2025
                            Common.FatorVencimento(boleto.DataVencimento),
                            valorBoletoTexto,
                            boleto.CedenteBoleto.CodigoCedente.PadLeft(7, '0'),
                            boleto.SequencialNossoNumero.PadLeft(13, '0'),
                            (boleto.DataVencimento.DayOfYear +
                             boleto.DataVencimento.ToString("yy").Substring(1, 1)).PadLeft(4, '0')
                            );
                }

                /* 
                 * 1. Calcula dígito de auto conferência
                 * 2. Insere no meio do código de barras
                 * 3. Atribui ao boleto
                 */
                string codigoBarraComDigitoVerificador = null;

                _digitoAutoConferenciaCodigoBarras = Common.Mod11(codigoBarraSemDigitoVerificador, 9, 0);

                codigoBarraComDigitoVerificador =
                    Common.Left(codigoBarraSemDigitoVerificador, 4) +
                    _digitoAutoConferenciaCodigoBarras +
                    Common.Right(codigoBarraSemDigitoVerificador, 39);

                boleto.CodigoBarraBoleto = codigoBarraComDigitoVerificador;
            }
            catch (Exception ex)
            {
                throw new Exception("Falha ao formatar código de barras.", ex);
            }
        }


        /// <summary>
        /// Calcula primeiro dígito verificador
        /// </summary>
        /// <param name="codigoPagador">Equivalente a número do documento.</param>
        /// <returns></returns>
        public string CalculaPrimeiroDigitoVerificadorCnrTipo4(string codigoPagador)
        {
            return Common.Mod11Base9(codigoPagador).ToString();
        }

        public string CalculaSegundoDigitoVerificadorCnrTipo4(string codigoPagador, string primeiroDigitoVerificador, string codigoBeneficiario, DateTime dataVencimento)
        {
            return Common.Mod11Base9(
                (
                    long.Parse(codigoPagador + primeiroDigitoVerificador + "4") +
                    long.Parse(codigoBeneficiario) +
                    long.Parse(dataVencimento.ToString("ddMMyy"))
                    )
                    .ToString()
                )
                .ToString();
        }


    }
}