using HSE.Automation.Services.Produtos;
using Xunit;

namespace HSE_Cadastros.Tests
{
    /// <summary>
    /// Comparação usada para saber se um Salvar sem resposta gravou o produto no HSE.
    /// Os exemplos são reais (cadastros de 26/11/2026).
    /// </summary>
    public class ConsultaProdutoHseTests
    {
        [Fact]
        public void Reconhece_descricao_cortada_em_100_caracteres_pelo_HSE()
        {
            const string pedido = "CHAVE ESTRELA  2 BOC 1;5/16X1 1/2POL; 400MM  STANDARD ACO CROMO VANADIO 003.565 GEODORE/ 2 -1 5/161 1/2";
            const string noHse  = "CHAVE ESTRELA  2 BOC 1;5/16X1 1/2POL; 400MM  STANDARD ACO CROMO VANADIO 003.565 GEODORE/ 2 -1 5/161 ";

            Assert.True(ConsultaProdutoHse.MesmaDescricao(pedido, noHse));
        }

        [Fact]
        public void Reconhece_descricao_igual_com_espacos_e_maiusculas_diferentes()
        {
            Assert.True(ConsultaProdutoHse.MesmaDescricao(
                "chave fix 1.5/16X1.1/2POL 331M ACO VANADIO 31CRV3 DIN 3110 15GR GEDORE 004.565",
                "CHAVE FIX 1.5/16X1.1/2POL 331M ACO VANADIO 31CRV3 DIN 3110 15GR  GEDORE 004.565"));
        }

        [Fact]
        public void Nao_confunde_produtos_parecidos()
        {
            Assert.False(ConsultaProdutoHse.MesmaDescricao(
                "CHAVE FIX 1.1/16 1.1/8POL 300MM ACO CROMO DIN 3110 15GR 1.1/16 1.1/POL GEDORE 004.561",
                "CHAVE FIX 1.5/16X1.1/2POL 331M ACO VANADIO 31CRV3 DIN 3110 15GR  GEDORE 004.565"));
            Assert.False(ConsultaProdutoHse.MesmaDescricao("CHAVE FIX", "CHAVE FIXA ISOLADA VDE DE 10MM GEDORE"));
            Assert.False(ConsultaProdutoHse.MesmaDescricao("", ""));
        }
    }
}
