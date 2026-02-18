// Save as: PlanilhaMarcasService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfficeOpenXml;

namespace HSE.Automation.Services
{
    public class PlanilhaMarcasService
    {
        private const string ExcelPath = @"C:\Users\meyri\Downloads\PLANILHA DE MARCAS.xlsx";

        public List<string> LerMarcasDaPlanilha()
        {
            var marcas = new List<string>();

            try
            {
                if (!File.Exists(ExcelPath))
                {
                    Console.WriteLine($"❌ Arquivo não encontrado: {ExcelPath}");
                    return marcas;
                }

                using (var package = new ExcelPackage(new FileInfo(ExcelPath)))
                {
                    var worksheet = package.Workbook.Worksheets[0]; // Primeira planilha
                    var rowCount = worksheet.Dimension?.Rows ?? 0;

                    Console.WriteLine($"📄 Lendo planilha: {rowCount} linhas encontradas");

                    for (int row = 2; row <= rowCount; row++) // Começa da linha 2 (pula cabeçalho)
                    {
                        var marca = worksheet.Cells[row, 1].Text?.Trim();

                        if (!string.IsNullOrWhiteSpace(marca) &&
                            !string.Equals(marca, "MARCAS", StringComparison.OrdinalIgnoreCase))
                        {
                            // Remove espaços extras e quebras de linha
                            marca = marca.Replace("\n", "").Replace("\r", "").Trim();

                            // Verifica se é uma marca válida (não é apenas espaços ou traços)
                            if (!string.IsNullOrWhiteSpace(marca.Replace("-", "").Replace("|", "")))
                            {
                                marcas.Add(marca);
                            }
                        }
                    }
                }

                // Remove duplicados mantendo a ordem
                marcas = marcas.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                Console.WriteLine($"✅ Total de marcas únicas encontradas: {marcas.Count}");

                // Exibe algumas marcas como exemplo
                if (marcas.Count > 0)
                {
                    Console.WriteLine("\n📝 Exemplo de marcas encontradas:");
                    for (int i = 0; i < Math.Min(5, marcas.Count); i++)
                    {
                        Console.WriteLine($"   {i + 1}. {marcas[i]}");
                    }
                    if (marcas.Count > 5)
                        Console.WriteLine($"   ... e mais {marcas.Count - 5} marcas");
                }

                return marcas;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro ao ler planilha: {ex.Message}");
                return marcas;
            }
        }
    }
}