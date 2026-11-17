using HSE.Automation.Services;
using Xunit;

namespace HSE_Cadastros.Tests
{
    /// <summary>
    /// A busca de marca monta uma expressão regular com o nome da marca. Nomes com símbolos
    /// (ex.: a marca "LENOVORACKSWITCHG8264CS(F-R)") quebravam a expressão e a marca não era escolhida.
    /// </summary>
    public class MarcaServiceTests
    {
        [Theory]
        [InlineData("switch lenovorackswitchg8264cs(r 48 portas", "lenovorackswitchg8264cs(r")]
        [InlineData("switch lenovorackswitchg8264cs(f-r) 48 portas", "lenovorackswitchg8264cs(f")]
        [InlineData("switch lenovorackswitchg8264cs(f-r) 48 portas", "r)")]
        [InlineData("cabo c++ 2m", "c++")]
        [InlineData("notebook dell latitude", "dell")]
        public void Encontra_palavra_com_caracteres_especiais(string descricao, string palavra)
        {
            Assert.True(MarcaService.DescricaoContemPalavraChave(descricao, palavra));
        }

        [Theory]
        [InlineData("switch lenovorackswitchg8264cs(rx 48 portas", "lenovorackswitchg8264cs(r")]
        [InlineData("notebook dellinspiron", "dell")]
        [InlineData("cabo hdmi 2m", "a.b")]
        public void Nao_encontra_quando_nao_e_palavra_inteira(string descricao, string palavra)
        {
            Assert.False(MarcaService.DescricaoContemPalavraChave(descricao, palavra));
        }
    }
}
