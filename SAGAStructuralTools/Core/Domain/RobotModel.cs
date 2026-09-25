using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Domain
{
    /// <summary>Nó do modelo do Robot, nas unidades declaradas no arquivo (ex.: metros).</summary>
    public class RobotNode
    {
        public int Id;
        public double X;
        public double Y;
        public double Z;
    }

    /// <summary>Barra do modelo do Robot: liga dois nós por ID.</summary>
    public class RobotBar
    {
        public int Id;
        public int StartNodeId;
        public int EndNodeId;
    }

    /// <summary>Perfil atribuído a uma barra, lido da seção PROperties.</summary>
    public class RobotProfileAssignment
    {
        public string Material;

        /// <summary>Designação do perfil como aparece no arquivo (ex.: "W 460x52", "U 250x100x3.75").</summary>
        public string RawDesignation;

        /// <summary>Rotação da seção transversal em torno do próprio eixo da barra (graus), se o arquivo declarar GAmma.</summary>
        public double? GammaDegrees;
    }

    /// <summary>Barra já resolvida: geometria (dois nós) + perfil atribuído. Só barras com
    /// perfil atribuído viram RobotMember — o resto (malha de piso/parede, por exemplo) é ignorado.</summary>
    public class RobotMember
    {
        public int BarId;
        public RobotNode Start;
        public RobotNode End;
        public RobotProfileAssignment Profile;
    }

    public class RobotParseResult
    {
        /// <summary>Unidade de comprimento declarada no arquivo (UNIts LENgth=...). Normalmente "m".</summary>
        public string LengthUnit = "m";

        public List<RobotMember> Members = new List<RobotMember>();

        /// <summary>Barras/linhas que não puderam ser resolvidas (sem perfil, nó inexistente, linha
        /// não reconhecida) — reportadas em vez de silenciosamente ignoradas, já que o formato do
        /// Robot pode variar entre versões/exportações e ainda não foi validado em mais de um arquivo.</summary>
        public List<string> Warnings = new List<string>();
    }
}
