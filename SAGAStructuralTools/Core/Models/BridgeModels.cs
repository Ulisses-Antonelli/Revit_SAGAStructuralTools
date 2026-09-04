using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Models
{
    public class BridgeTriangle
    {
        public double[] v0 { get; set; }
        public double[] v1 { get; set; }
        public double[] v2 { get; set; }
    }

    public class BridgeItem
    {
        public string origem { get; set; }
        public string item { get; set; }
        public string unidade { get; set; }
        public List<BridgeTriangle> malha { get; set; } = new List<BridgeTriangle>();
    }

    /// <summary>
    /// Referência de coordenadas do arquivo-ponte. A malha dos itens já vem
    /// relativa ao ponto clicado no Navisworks (esse ponto é o 0,0,0 do arquivo).
    /// </summary>
    public class BridgeReference
    {
        /// <summary>
        /// Vetor de direção (mm, eixos do Navisworks) do ponto de referência até um
        /// segundo ponto opcional, usado para corrigir rotação na importação.
        /// Nulo quando a exportação não definiu direção — aí a rotação é zero.
        /// </summary>
        public double[] direcao { get; set; }
    }

    public class BridgeFile
    {
        public BridgeReference referencia { get; set; }
        public List<BridgeItem> itens { get; set; } = new List<BridgeItem>();
    }
}
