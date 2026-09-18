namespace SAGAStructuralTools.UI
{
    public static class ToolTipTexts
    {
        public const string CompressionForce = "Força normal de compressão na base. Informar somente valores positivos (+). Utilize zero quando não houver compressão.";
        public const string TensionForce = "Força normal de tração na base. Informar somente valores negativos (-). Utilize zero quando não houver tração.";
        public const string MomentX = "Momento atuando em torno do eixo x (somente valores positivos)";
        public const string MomentY = "Momento atuando em torno do eixo y (somente valores positivos)";
        public const string ShearX = "Cortante no sentido do eixo x (somente valores positivos)";
        public const string ShearY = "Cortante no sentido do eixo y (somente valores positivos)";
        public const string AnchorDiameter = "Diâmetro adotado para chumbadores";
        public const string AnchorLength = "Comprimento adotado para chumbadores";
        public const string AnchorHook = "Fixação do chumbador com gancho ou reto";
        public const string AnchorCorrosion = "Espessura de corrosão dos chumbadores";
        public const string AnchorFy = "Tensão de escoamento do chumbador";
        public const string AnchorFu = "Tensão última do chumbador";
        public const string AnchorsX = "Numero de chumbadores que resistem ao momento em torno de x";
        public const string AnchorsY = "Numero de chumbadores que resistem ao momento em torno de y";
        public const string TotalAnchors = "Número total de chumbadores na placa de base.";
        public const string AnchorEdgeDistanceX = "Distância furo-borda a1";
        public const string AnchorEdgeDistanceY = "Distância furo-borda b1";
        public const string PlateLengthX = "Dimensão lx da placa de base";
        public const string PlateLengthY = "Dimensão ly da placa de base";
        public const string PlateOrientation = "Rotaciona a placa, furos e chumbadores em relacao aos eixos locais do pilar. Escolha 0º ou 90º.";
        public const string PlateThickness = "Espessura da placa de base";
        public const string PlateFy = "Tensão de escoamento da placa de base e nervuras";
        public const string PlateFu = "Tensão última da placa de base e nervuras";
        public const string StiffenerThickness = "Espessura das chapas de nervura";
        public const string StiffenerHeight = "Altura das chapas de nervura.";
        public const string MiddleStiffeners = "Existência ou não se nervura adicional nos eixos centrais da placa de base";
        public const string ConcreteFck = "Fck do concreto";
        public const string ConcreteAreaRatio = "Relação entre área projetada de concreto e área da placa de base. Valor 1 é conservador em relação a valores maiores. Não pode ser inferior a 1.";
        public const string ConcreteEdgeDistanceX = "Distância do eixo do chumbador até a borda livre do bloco ou pedestal de concreto, no sentido X.";
        public const string ConcreteEdgeDistanceY = "Distância do eixo do chumbador até a borda livre do bloco ou pedestal de concreto, no sentido Y.";
        public const string CreateButton = "Criação ainda desabilitada nesta etapa";
        public const string ConcreteLengthX = "Dimensao total do bloco ou pedestal de concreto no sentido X. A placa e considerada centralizada para calcular cx.";
        public const string ConcreteLengthY = "Dimensao total do bloco ou pedestal de concreto no sentido Y. A placa e considerada centralizada para calcular cy.";
        public const string CloseButton = "Fecha a janela da ferramenta";
    }
}
