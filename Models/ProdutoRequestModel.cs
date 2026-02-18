﻿using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace HSE.Automation.Models
{
    public class ProdutoRequestModel
    {
        [JsonPropertyName("requestId")]
        public string RequestId { get; set; }

        [JsonPropertyName("descricao")]
        public string Descricao { get; set; }

        [JsonPropertyName("ncm")]
        public string NCM { get; set; }

        [JsonPropertyName("custo")]
        public decimal Custo { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;

        public int Prioridade { get; set; } = 1;

        public int Tentativas { get; set; } = 0;

        // Valida se os dados mínimos estão presentes
        public bool Valido()
        {
            if (string.IsNullOrEmpty(Descricao) ||
                string.IsNullOrEmpty(NCM) ||
                Custo <= 0)
            {
                return false;
            }

            // Remove pontos e espaços do NCM para validação
            string ncmLimpo = NCM.Replace(".", "").Replace(" ", "");

            // Aceita NCMs com 8 dígitos numéricos
            if (ncmLimpo.Length == 8 && ncmLimpo.All(char.IsDigit))
            {
                return true;
            }

            // Se não tem 8 dígitos, registra o problema mas não falha imediatamente
            Console.WriteLine($"⚠️ NCM '{NCM}' -> '{ncmLimpo}' não tem 8 dígitos ({ncmLimpo.Length} dígitos)");

            // Permite continuar mesmo com NCM inválido (o usuário pode corrigir)
            // Mas marca como inválido para o parser
            return false;
        }

        public override string ToString()
        {
            return $"Produto: {Descricao} | NCM: {NCM} | Custo: R$ {Custo:F2} | ID: {RequestId}";
        }
    }
}