﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BoletoBr.Bancos.Hsbc;

namespace BoletoBr.Bancos.Amazonia
{
    public class BancoAmazonia : IBanco
    {
        public string CodigoBanco { get; set; }
        public string DigitoBanco { get; set; }
        public string NomeBanco { get; set; }

        public BancoAmazonia()
        {
            CodigoBanco = "3";
            DigitoBanco = "5";
            NomeBanco = "Banco da Amazônia S/A";

            /* Adiciona carteiras de cobrança */
            _carteirasCobrancaBasa = new List<CarteiraCobranca>();
            _carteirasCobrancaBasa.Add(new CarteiraCobrancaBancoAmazoniaCnr());
            _carteirasCobrancaBasa.Add(new CarteiraCobrancaBancoAmazoniaCsb());
        }

        private string _dacNossoNumero = string.Empty;
        private int _dacBoleto = 0;
        private readonly List<CarteiraCobranca> _carteirasCobrancaBasa;

        public List<CarteiraCobranca> GetCarteirasCobranca()
        {
            return _carteirasCobrancaBasa;
        }

        public CarteiraCobranca GetCarteiraCobrancaPorCodigo(string codigoCarteira)
        {
            return GetCarteirasCobranca().Find(fd => fd.Codigo == codigoCarteira);
        }

        public void FormataLinhaDigitavel(Boleto boleto)
        {
            string nossonumero = boleto.SequencialNossoNumero.PadLeft(16, '0');

            # region GRUPO 1

            string banco = this.CodigoBanco.PadLeft(3, '0');
            string moeda = boleto.Moeda.ToString("0");
            string agencia = Common.Right(boleto.CedenteBoleto.ContaBancariaCedente.Agencia, 3) +
                boleto.CedenteBoleto.ContaBancariaCedente.DigitoAgencia;

            string convenio = boleto.CedenteBoleto.Convenio.PadLeft(4, '0');

            string convenioParte1 = convenio.Substring(0, 1);

            string dv1 = Mod10_LinhaDigitavel(banco + moeda + agencia + convenioParte1).ToString("0");

            string grupo1 = banco + moeda + agencia + convenioParte1 + dv1;

            #endregion GRUPO 1

            #region GRUPO 2

            string convenioParte2 = convenio.Substring(1, 3);
            string nossonumeroParte1 = nossonumero.Substring(0, 7);

            string dv2 = Mod10_LinhaDigitavel(convenioParte2 + nossonumeroParte1).ToString("0");

            string grupo2 = convenioParte2 + nossonumeroParte1 + dv2;

            #endregion GRUPO 2

            #region GRUPO 3

            string nossonumeroParte2 = nossonumero.Substring(7, 9);
            const string identificadorsistema = "8";

            string dv3 = Mod10_LinhaDigitavel(nossonumeroParte2 + identificadorsistema).ToString("0");

            string grupo3 = nossonumeroParte2 + identificadorsistema + dv3;

            #endregion GRUPO 3

            #region GRUPO 4

            string dvCodigobarra = boleto.CodigoBarraBoleto.Substring(4, 1);

            string grupo4 = dvCodigobarra;

            #endregion GRUPO 4

            #region GRUPO 5

            string fatorvencimento = Common.FatorVencimento(boleto.DataVencimento).ToString();
            string valordocumento = boleto.ValorBoleto.ToString("f").Replace(",", "").Replace(".", "").PadLeft(10, '0');

            string grupo5 = fatorvencimento + valordocumento;

            #endregion GRUPO 5

            string ldb = grupo1 + grupo2 + grupo3 + grupo4 + grupo5;

            boleto.LinhaDigitavelBoleto =
                ldb.Substring(0, 5) + "." +
                ldb.Substring(5, 5) + " " +
                ldb.Substring(10, 5) + "." +
                ldb.Substring(15, 6) + "  " +
                ldb.Substring(21, 5) + "." +
                ldb.Substring(26, 6) + " " +
                ldb.Substring(32, 1) + " " +
                ldb.Substring(33, 14);
        }

        public void FormataCodigoBarra(Boleto boleto)
        {
            // Código de Barras
            //banco & moeda & fator & valor & carteira & nossonumero & dac_nossonumero & agencia & conta & dac_conta & "000"

            string banco = this.CodigoBanco.PadLeft(3, '0');
            int moeda = boleto.Moeda;
            //string digito = "";
            string valorBoleto = boleto.ValorBoleto.ToString("f").Replace(",", "").Replace(".", "");
            valorBoleto = valorBoleto.PadLeft(10, '0');

            string fatorvencimento = Common.FatorVencimento(boleto.DataVencimento).ToString();

            string agencia = Common.Right(boleto.CedenteBoleto.ContaBancariaCedente.Agencia, 3) +
                boleto.CedenteBoleto.ContaBancariaCedente.DigitoAgencia;
            string convenio = boleto.CedenteBoleto.Convenio.PadLeft(4, '0');
            string nossonumero = boleto.SequencialNossoNumero.PadLeft(16, '0');

            // Este numero foi fornecido pelo BASA para o convenio testado.. nao sei se muda.

            /*
             * Conforme documentação técnica do BASA o identificador será sempre '8'.
             * Samuel R. (samuelro.net@gmail.com)
             */
            const string identificadorsistema = "8";
                
            boleto.CodigoBarraBoleto = string.Format("{0}{1}{2}{3}",
                banco, moeda, fatorvencimento, valorBoleto);

            boleto.CodigoBarraBoleto += string.Format("{0}{1}{2}{3}",
                agencia, convenio, nossonumero, identificadorsistema);

            _dacBoleto = Mod11_CodigoBarra(boleto.CodigoBarraBoleto, 9);

            boleto.CodigoBarraBoleto = Common.Left(boleto.CodigoBarraBoleto, 4) + _dacBoleto +
                                        Common.Right(boleto.CodigoBarraBoleto, 39);
        }

        //public override string GerarHeaderRemessa(string numeroConvenio, Cedente cedente, TipoArquivo tipoArquivo,
        //    int numeroArquivoRemessa, Boleto boletos)
        //{
        //    throw new NotImplementedException("Fun��o n�o implementada.");
        //}

        public string FormataCampoLivre(Boleto boleto)
        {
            throw new NotImplementedException();
        }


        public void FormataNumeroDocumento(Boleto boleto)
        {
            boleto.NumeroDocumento = string.Format("{0}", boleto.NumeroDocumento);
        }


        public void FormataNossoNumero(Boleto boleto)
        {
            boleto.SetNossoNumeroFormatado(string.Format("{0}", boleto.SequencialNossoNumero.PadLeft(16, '0')));
        }

        public void FormatarBoleto(Boleto boleto)
        {
            boleto.ValidaDadosEssenciaisDoBoleto();

            ValidaBoletoComNormasBanco(boleto);

            //Atribui o nome do banco ao local de pagamento
            boleto.LocalPagamento = "Pagável em qualquer banco até o vencimento. Após o vencimento pagar apenas nas agências do Banco da Amazônia S/A";

            FormataNumeroDocumento(boleto);
            FormataNossoNumero(boleto);
            FormataLinhaDigitavel(boleto);
            FormataCodigoBarra(boleto);
        }

        public void ValidaBoletoComNormasBanco(Boleto boleto)
        {
            //Verifica as carteiras implementadas
            if (!boleto.CarteiraCobranca.Codigo.Equals("CNR"))
                throw new NotImplementedException("Carteira não implementada. Utilize a carteira 'CNR'.");

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

        public void ValidaBoleto(Boleto boleto)
        {
            //Verifica se data do processamento � valida
            //if (boleto.DataProcessamento.ToString("dd/MM/yyyy") == "01/01/0001")
            if (boleto.DataProcessamento == DateTime.MinValue) // diegomodolo (diego.ribeiro@nectarnet.com.br)
                boleto.DataProcessamento = DateTime.Now;


            //Verifica se data do documento � valida
            //if (boleto.DataDocumento.ToString("dd/MM/yyyy") == "01/01/0001")
            if (boleto.DataDocumento == DateTime.MinValue) // diegomodolo (diego.ribeiro@nectarnet.com.br)
                boleto.DataDocumento = DateTime.Now;

            FormataNossoNumero(boleto);
            FormataNumeroDocumento(boleto);
            FormataCodigoBarra(boleto);
            FormataLinhaDigitavel(boleto);
        }

        #region M�todos privados

        private int Mod11_CodigoBarra(string value, int Base)
        {
            int Digito, Soma = 0, Peso = 2;
            for (int i = value.Length; i > 0; i--)
            {
                Soma = Soma + (Convert.ToInt32(Common.Mid(value, i, 1)) * Peso);
                if (Peso == Base)
                    Peso = 2;
                else
                    Peso = Peso + 1;
            }
            if (((Soma % 11) == 0) || ((Soma % 11) == 10) || ((Soma % 11) == 1))
            {
                Digito = 1;
            }
            else
            {
                Digito = 11 - (Soma % 11);
            }
            return Digito;
        }

        private int Mod10_LinhaDigitavel(string seq)
        {
            int Digito, Soma = 0, Peso = 2, m1;
            string m2;
            for (int i = seq.Length; i > 0; i--)
            {
                m1 = (Convert.ToInt32(Common.Mid(seq, i, 1))*Peso);
                m2 = m1.ToString();

                for (int j = 1; j <= m2.Length; j++)
                {
                    Soma += Convert.ToInt32(Common.Mid(m2, j, 1));
                }

                if (Peso == 2)
                    Peso = 1;
                else
                    Peso = Peso + 1;
            }  
            Digito = ((10 - (Soma % 10)) % 10);
            return Digito;
        }

        #endregion

        /// <summary>
        /// Efetua as Valida��es dentro da classe Boleto, para garantir a geração da remessa
        /// </summary>
        //public override bool ValidarRemessa(TipoArquivo tipoArquivo, string numeroConvenio, IBanco banco,
        //    Cedente cedente, Boletos boletos, int numeroArquivoRemessa, out string mensagem)
        //{
        //    bool vRetorno = true;
        //    string vMsg = string.Empty;
        //    ////IMPLEMENTACAO PENDENTE...
        //    mensagem = vMsg;
        //    return vRetorno;
        //}
    }
}