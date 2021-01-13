﻿using BoletoBr.Interfaces;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BoletoBr.Arquivo.CNAB240.Remessa;
using BoletoBr.Arquivo.CNAB400.Remessa;
using BoletoBr.Arquivo.Generico.Retorno;
using BoletoBr.Bancos.Itau;
using BoletoBr.Dominio;
using BoletoBr.Enums;

namespace BoletoBr.Bancos.Banrisul
{
    public class BancoBanrisul : IBanco
    {
        public string CodigoBanco { get; set; }
        public string DigitoBanco { get; set; }
        public string NomeBanco { get; set; }
        public Image LogotipoBancoParaExibicao { get; set; }
        public string LocalDePagamento { get; private set; }
        public string MoedaBanco { get; private set; }

        public BancoBanrisul()
        {
            CodigoBanco = "041";
            DigitoBanco = "8";
            NomeBanco = "Banrisul";
            LocalDePagamento = "Pagável em qualquer banco até o vencimento";
            MoedaBanco = "9";
        }

        public void ValidaBoletoComNormasBanco(Boleto boleto)
        {
            /* TIPO DE CARTEIRA:
            * - Campo alfanumérico obrigatório.
            * - Para Tipos de Carteira diferentes de 1, contratar antecipadamente com a sua Agência.
            * - Conteúdo:
            * 1 – Cobrança Simples (8050.76)
            * 3 – Cobrança Caucionada (8150.55) Reservado
            * 4 – Cobrança em IGPM (8450.94) *
            * 5 – Cobrança Caucionada CGB Especial (8355.01) Reservado
            * 6 – Cobrança Simples Seguradora (8051.57)
            * 7 – Cobrança em UFIR (8257.86) *
            * 8 – Cobrança em IDTR (8356.84) *
            * C – Cobrança Vinculada (8250.34)
            * D – Cobrança CSB (8258.67)
            * E – Cobrança Caucionada Câmbio (8156.24)
            * F – Cobrança Vendor (8152.17) Reservado
            * H – Cobrança Caucionada Dólar (8157.05) Reservado **
            * I – Cobrança Caucionada Compror (8351.46) Reservado
            * K – Cobrança Simples INCC-M (8153.06)
            * M – Cobrança Partilhada (8154.70)
            * N – Capital de Giro CGB ICM (6130.96) Reservado
            * R – Desconto de Duplicata (6030.15) ***
            * S – Vendor Eletrônico – Valor Final (Corrigido) (6032.79) ***
            */
            var carteirasValidas = new List<string>() { "1", "3", "4", "5", "6", "7", "8", "C", "D", "E", "F", "H", "I", "K", "M", "N", "R", "S" };
            if (!carteirasValidas.Contains(boleto.CarteiraCobranca.Codigo))
                throw new ApplicationException("Codigo carteira invalido.");
        }

        public void FormataMoeda(Boleto boleto)
        {
            boleto.Moeda = MoedaBanco;

            if (String.IsNullOrEmpty(boleto.Moeda))
                throw new Exception("Espécie/Moeda para o boleto não foi informada.");

            if ((boleto.Moeda == "9") || (boleto.Moeda == "REAL") || (boleto.Moeda == "R$"))
                boleto.Moeda = "R$";
        }

        public void FormatarBoleto(Boleto boleto)
        {
            //Atribui o local de pagamento
            //boleto.LocalPagamento = LocalDePagamento;

            boleto.ValidaDadosEssenciaisDoBoleto();

            FormataNumeroDocumento(boleto);
            FormataNossoNumero(boleto);
            FormataCodigoBarra(boleto);
            FormataLinhaDigitavel(boleto);
            /*Inicialmente não utilizado*/
            //FormataMoeda(boleto);
            
            ValidaBoletoComNormasBanco(boleto);

            /* Formata o código do cedente
             * Inserindo o dígito verificador
             */
            string codigoCedente = boleto.CedenteBoleto.CodigoCedente.PadLeft(6, '0');
            string dvCodigoCedente = Common.Mod11Base9(codigoCedente).ToString(CultureInfo.InvariantCulture); //Base9 

            if (boleto.CedenteBoleto.DigitoCedente.Equals(-1))
                boleto.CedenteBoleto.DigitoCedente = Convert.ToInt32(dvCodigoCedente);

            boleto.CedenteBoleto.CodigoCedenteFormatado = String.Format("{0}/{1}",
                boleto.CedenteBoleto.ContaBancariaCedente.Agencia.PadLeft(4, '0'),
                codigoCedente);
        }

        public void FormataCodigoBarra(Boleto boleto)
        {
            try
            {
                /* Produto: */
                var constanteCCCC = "0419";
                /*dados*/
                var constanteProduto = boleto.CarteiraCobranca.BancoEmiteBoleto ? "1" : "2";
                var argencia = boleto.CedenteBoleto.ContaBancariaCedente.Agencia.Replace(".", "").Replace("/", "").Replace("-", "").Replace(",", "").PadLeft(4, '0');
                var codigoCedente = boleto.CedenteBoleto.CodigoCedente.Substring(0, 7);
                var nossoNumero = boleto.IdentificadorInternoBoleto.PadLeft(8, '0');
                /*dados formatados*/
                var dadosFormatados = $@"{constanteProduto}1{argencia}{codigoCedente}{nossoNumero}40";
                var DVNCDadosFormatados = Common.DigitoVerificadorBanrisulNC(dadosFormatados);
                var fatorVencimento = Common.FatorVencimento(boleto.DataVencimento).ToString().PadLeft(4,'0');
                string valorBoleto = boleto.ValorBoleto.ToString("f").Replace(",", "").Replace(".", "").PadLeft(10, '0');

                /*
                    O dígito de autoconferência – DAC – ocupa a posição 5 do código de barras, conforme especificações
                    padrão Febraban.
                    Para o cálculo é feito utilizando o módulo 11, porém, com pesos diferentes do segundo dígito verificador.
                    Considerar as posições de ->> (1 a 4) e de ->> (6 a 44) do código de barras e atribuir pesos de 2 a 9 da direita para a
                    esquerda.
                   */
                var DAC = Common.Mod11($@"{constanteCCCC}{fatorVencimento}{valorBoleto}{dadosFormatados}{DVNCDadosFormatados}", 9);

                boleto.CodigoBarraBoleto =
                        string.Format("{0}{1}{2}{3}{4}{5}",
                            constanteCCCC, /*0*/
                            DAC,/*1*/
                            fatorVencimento, /*2*/
                            valorBoleto,/*3*/
                            dadosFormatados,/*4*/
                            DVNCDadosFormatados /*5*/
                        );
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("<BoletoBr>" +
                                                  "{0}Mensagem: Falha ao formatar código de barras.",
                    Environment.NewLine), ex);
            }
        }

        public void FormataLinhaDigitavel(Boleto boleto)
        {
            try
            {
                var constanteCCCC = "0419";
                /*Grupo 1*/
                var constanteCodBanco = "041";
                var moeda = "9";
                var constanteProduto = boleto.CarteiraCobranca.BancoEmiteBoleto ? "1" : "2";
                var grupo1 = $@"{constanteCodBanco}{moeda}{constanteProduto}";
                /*Grupo 2*/
                var argencia = boleto.CedenteBoleto.ContaBancariaCedente.Agencia.Replace(".", "").Replace("/", "").Replace("-", "").Replace(",", "").PadLeft(4, '0');
                var grupo2 = $@"1{argencia.Substring(0,3)}{Common.Mod10( $@"{grupo1}1{argencia.Substring(0,3)}")}";
                /*Grupo 3*/
                var codigoCedente = boleto.CedenteBoleto.CodigoCedente.Substring(0, 7);
                var grupo3 = $@"{argencia.Substring(3,1)}{codigoCedente.Substring(0,4)}";
                /*Grupo 4*/
                var nossoNumero = boleto.IdentificadorInternoBoleto.PadLeft(8, '0');
                var grupo4SemDigito = $@"{codigoCedente.Substring(4, 3)}{nossoNumero.Substring(0, 2)}";
                var grupo4 = $@"{grupo4SemDigito}{Common.Mod10($@"{grupo3}{grupo4SemDigito}")}";
                /*Grupo 5*/
                var grupo5 = $@"{nossoNumero.Substring(2, 5)}";
                /*Grupo 6*/
                var dadosFormatados = $@"{constanteProduto}1{argencia}{codigoCedente}{nossoNumero}40";
                var XX = Common.DigitoVerificadorBanrisulNC(dadosFormatados);
                var grupo6SemDigito = $@"{nossoNumero.Substring(7, 1)}40{XX}";
                var grupo6 = $@"{grupo6SemDigito}{Common.Mod10($@"{grupo5}{grupo6SemDigito}")}";
                /*grupo 8*/
                string valorBoleto = boleto.ValorBoleto.ToString("f").Replace(",", "").Replace(".", "").PadLeft(10, '0');
                var fatorVencimento = Common.FatorVencimento(boleto.DataVencimento).ToString().PadLeft(4, '0');
                var grupo8 = $@"{fatorVencimento}{valorBoleto}";

                /*Grupo 7*/
                var DAC = Common.Mod11($@"{constanteCCCC}{fatorVencimento}{valorBoleto}{dadosFormatados}{XX}", 9);

                boleto.LinhaDigitavelBoleto =
                    string.Format("{0}.{1} {2}.{3} {4}.{5} {6} {7}",
                        grupo1, /*0*/
                        grupo2,/*1*/
                        grupo3, /*2*/
                        grupo4,/*3*/
                        grupo5,/*4*/
                        grupo6, /*5*/
                        DAC,/*6*/
                        grupo8/*7*/
                    );
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("<BoletoBr>{0}Mensagem: Falha ao formatar linha digitavel.", Environment.NewLine), ex);
            }
        }

        public void FormataNossoNumero(Boleto boleto)
        {
            if (String.IsNullOrEmpty(boleto.IdentificadorInternoBoleto))
                throw new Exception("Sequencial nosso número não pode estar em branco.");

            if (boleto.IdentificadorInternoBoleto.Length > 15)
                throw new Exception("Sequencial nosso número não pode exceder 15 dígitos.");

            string dvNossoNumeroNC;
            
            boleto.SetNossoNumeroFormatado((boleto.IdentificadorInternoBoleto.Length > 8 ? boleto.IdentificadorInternoBoleto.Substring(0, 8) : boleto.IdentificadorInternoBoleto).PadLeft(8, '0'));
            
            dvNossoNumeroNC = Common.DigitoVerificadorBanrisulNC(boleto.NossoNumeroFormatado).ToString(CultureInfo.InvariantCulture).PadLeft(2, '0');
            
            boleto.SetNossoNumeroFormatado(string.Format("{0}-{1}", boleto.NossoNumeroFormatado, dvNossoNumeroNC));
        }

        public void FormataNumeroDocumento(Boleto boleto)
        {
            if (String.IsNullOrEmpty(boleto.NumeroDocumento) || String.IsNullOrEmpty(boleto.NumeroDocumento.TrimStart('0')))
                throw new Exception("Número do Documento não foi informado.");
            string numeroDoDocumento;
            if (boleto.NumeroDocumento.Length > 8)
                numeroDoDocumento = boleto.NumeroDocumento.Substring(0, 13);
            else
                numeroDoDocumento = boleto.NumeroDocumento.PadLeft(13, '0');

            boleto.NumeroDocumento = numeroDoDocumento;

        }

        public ICodigoOcorrencia ObtemCodigoOcorrenciaByInt(int numeroOcorrencia)
        {
            switch (numeroOcorrencia)
            {
                case 02:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "CONFIRMAÇÃO DE ENTRADA"
                    };
                case 03:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "ENTRADA REJEITADA"
                    };
                case 04:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "BAIXA DE TÍTULO LIQUIDADO POR EDITAL"
                    };
                case 06:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "LIQUIDAÇÃO NORMAL"
                    };
                case 07:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "LIQUIDAÇÃO PARCIAL"
                    };
                case 08:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "BAIXA POR PAGAMENTO, LIQUIDAÇÃO PELO SALDO"
                    };
                case 09:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "DEVOLUÇÃO AUTOMÁTICA"
                    };
                case 10:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "BAIXADO CONFORME INSTRUÇÕES"
                    };
                case 11:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "ARQUIVO LEVANTAMENTO"
                    };
                case 12:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "CONCESSÃO DE ABATIMENTO"
                    };
                case 13:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "CANCELAMENTO DE ABATIMENTO"
                    };
                case 14:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "VENCIMENTO ALTERADO"
                    };
                case 15:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "PAGAMENTO EM CARTÓRIO"
                    };
                case 16:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "ALTERAÇÃO DE DADOS (*)"
                    };
                case 18:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "ALTERAÇÃO DE INSTRUÇÕES (*)"
                    };
                case 19:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "CONFIRMAÇÃO DE INSTRUÇÃO PROTESTO"
                    };
                case 20:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "CONFIRMAÇÃO DE INSTRUÇÃO PARA SUSTAR PROTESTO"
                    };
                case 21:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "AGUARDANDO AUTORIZAÇÃO PARA PROTESTO POR EDITAL"
                    };
                case 22:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "PROTESTO SUSTADO POR ALTERAÇÃO DE VENCIMENTO E PRAZO DE CARTÓRIO"
                    };
                case 23:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "CONFIRMAÇÃO DA ENTRADA EM CARTÓRIO"
                    };
                case 24:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "ALTERAÇÕES DE DADOS DO SACADOR"
                    };
                case 25:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "DEVOLUÇÃO, LIQUIDADO ANTERIORMENTE"
                    };
                case 26:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "DEVOLVIDO PELO CARTÓRIO – ERRO DE INFORMAÇÃO."
                    };
                case 30:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "COBRANÇA A CREDITAR (LIQUIDAÇÃO EM TRÂNSITO) (**)"
                    };
                case 31:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "TÍTULO EM TRÂNSITO PAGO EM CARTÓRIO (**)"
                    };
                case 32:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "REEMBOLSO E TRANSFERÊNCIA DESCONTO E VENDOR OU CARTEIRA EM GARANTIA"
                    };
                case 33:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "REEMBOLSO E DEVOLUÇÃO DESCONTO E VENDOR"
                    };
                case 34:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "REEMBOLSO NÃO EFETUADO POR FALTA DE SALDO"
                    };
                case 40:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "BAIXA DE TÍTULOS PROTESTADOS (**)"
                    };
                case 41:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "DESPESA DE APONTE. (**)"
                    };
                case 42:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "ALTERAÇÃO DE TÍTULO"
                    };
                case 43:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "RELAÇÃO DE TÍTULOS"
                    };
                case 44:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "MANUTENÇÃO MENSAL"
                    };
                case 45:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "SUSTAÇÃO DE CARTÓRIO E ENVIO DE TÍTULO A CARTÓRIO"
                    };
                case 46:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "FORNECIMENTO DE FORMULÁRIO PRÉ-IMPRESSO"
                    };
                case 47:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "CONFIRMAÇÃO DE ENTRADA – PAGADOR DDA (**)"
                    };
                case 68:
                    return new CodigoOcorrencia(numeroOcorrencia)
                    {
                        Codigo = numeroOcorrencia,
                        Descricao = "ACERTO DOS DADOS DO RATEIO DE CRÉDITO"
                    };
            }
            throw new Exception(
                String.Format(
                    "Não foi possível obter Código de Comando/Movimento/Ocorrência. Banco: {0} Código: {1}",
                    CodigoBanco, numeroOcorrencia.ToString()));
        }

        public ICodigoOcorrencia ObtemCodigoOcorrencia(EnumCodigoOcorrenciaRemessa ocorrencia, double valorOcorrencia,
            DateTime dataOcorrencia)
        {
            switch (ocorrencia)
            {
                case EnumCodigoOcorrenciaRemessa.Registro:
                    {
                        return new CodigoOcorrencia((int)ocorrencia)
                        {
                            Codigo = 01,
                            Descricao = "Remessa"
                        };
                    }
                case EnumCodigoOcorrenciaRemessa.Baixa:
                    {
                        return new CodigoOcorrencia((int)ocorrencia)
                        {
                            Codigo = 02,
                            Descricao = "Pedido de baixa"
                        };
                    }
                case EnumCodigoOcorrenciaRemessa.ConcessaoDeAbatimento:
                    {
                        return new CodigoOcorrencia((int)ocorrencia)
                        {
                            Codigo = 04,
                            Descricao = "Concessão de abatimento"
                        };
                    }
                case EnumCodigoOcorrenciaRemessa.CancelamentoDeAbatimento:
                    {
                        return new CodigoOcorrencia((int)ocorrencia)
                        {
                            Codigo = 05,
                            Descricao = "Cancelamento de abatimento"
                        };
                    }
                case EnumCodigoOcorrenciaRemessa.AlteracaoDeVencimento:
                    {
                        return new CodigoOcorrencia((int)ocorrencia)
                        {
                            Codigo = 06,
                            Descricao = "Alteração de vencimento"
                        };
                    }
                case EnumCodigoOcorrenciaRemessa.AlteracaoDoControleDoParticipante:
                    {
                        return new CodigoOcorrencia((int)ocorrencia)
                        {
                            Codigo = 07,
                            Descricao = "Alteração de uso empresa"
                        };
                    }
                case EnumCodigoOcorrenciaRemessa.AlteracaoSeuNumero:
                    {
                        return new CodigoOcorrencia((int)ocorrencia)
                        {
                            Codigo = 08,
                            Descricao = "Alteração do Seu Número"
                        };
                    }
                case EnumCodigoOcorrenciaRemessa.Protesto:
                    {
                        return new CodigoOcorrencia((int)ocorrencia)
                        {
                            Codigo = 09,
                            Descricao = "Protestar imediatamente"
                        };
                    }
                case EnumCodigoOcorrenciaRemessa.SustarProtesto:
                    {
                        return new CodigoOcorrencia((int)ocorrencia)
                        {
                            Codigo = 10,
                            Descricao = "Sustação de protesto"
                        };
                    }
                case EnumCodigoOcorrenciaRemessa.NaoCobrarJurosDeMora:
                    {
                        return new CodigoOcorrencia((int)ocorrencia)
                        {
                            Codigo = 11,
                            Descricao = "Não cobrar juros de mora"
                        };
                    }

                case EnumCodigoOcorrenciaRemessa.AlteracaoNomeEEnderecoSacado:
                    {
                        return new CodigoOcorrencia((int)ocorrencia)
                        {
                            Codigo = 14,
                            Descricao = "Dados do sacador"
                        };
                    }
                case EnumCodigoOcorrenciaRemessa.ProtestoParaFinsFalimentares:
                    {
                        return new CodigoOcorrencia((int)ocorrencia)
                        {
                            Codigo = 17,
                            Descricao = "Protestar imediatamente para fins de falência"
                        };
                    }
            }
            throw new Exception(
                String.Format(
                    "Não foi possível obter Código de Comando/Movimento/Ocorrência. Banco: {0} Código: {1}",
                    CodigoBanco, ocorrencia.ToString()));
            
        }

        public ICodigoOcorrencia ObtemCodigoOcorrencia(EnumCodigoOcorrenciaRetorno ocorrenciaRetorno)
        {
            throw new NotImplementedException();
        }

        public IEspecieDocumento ObtemEspecieDocumento(EnumEspecieDocumento especie)
        {
            throw new NotImplementedException();
        }

        public IInstrucao ObtemInstrucaoPadronizada(EnumTipoInstrucao tipoInstrucao, double valorInstrucao, DateTime dataInstrucao,
            int diasInstrucao)
        {
            throw new NotImplementedException();
        }

        public RetornoGenerico LerArquivoRetorno(List<string> linhasArquivo)
        {

            if (linhasArquivo == null || linhasArquivo.Any() == false)
                throw new ApplicationException("Arquivo informado é inválido/Não existem títulos no retorno.");
            
            if (linhasArquivo.First().Length == 400)
            {
                var leitor = new LeitorRetornoCnab400Banrisul(linhasArquivo);
                var retornoProcessado = leitor.ProcessarRetorno();

                var objRetornar = new RetornoGenerico(retornoProcessado);
                return objRetornar;
            }

            throw new Exception("Arquivo de RETORNO com " + linhasArquivo.First().Length + " posições, não é suportado.");

        }

        public RemessaCnab240 GerarArquivoRemessaCnab240(List<Boleto> boletos)
        {
            throw new NotImplementedException();
        }

        public RemessaCnab400 GerarArquivoRemessaCnab400(List<Boleto> boletos)
        {
            throw new NotImplementedException();
        }

        public int CodigoJurosMora(CodigoJurosMora codigoJurosMora)
        {
            return 0;
        }

        public int CodigoProteso(bool protestar = true)
        {
            return 0;
        }
    }
}
