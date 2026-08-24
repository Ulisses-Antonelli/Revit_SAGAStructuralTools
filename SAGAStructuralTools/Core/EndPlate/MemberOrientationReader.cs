using Autodesk.Revit.DB;
using SAGAStructuralTools.Core.Alignment;
using System;

namespace SAGAStructuralTools.Core.EndPlate
{
    /// <summary>
    /// Resolve as duas extremidades e o referencial local (largura/altura da
    /// seção) de uma viga ou pilar.
    /// </summary>
    internal static class MemberOrientationReader
    {
        /// <summary>
        /// A linha/ponto de desenho (LocationCurve/LocationPoint) não é a face
        /// física real do corte — pode estar recuada ou avançada em relação a
        /// ela (join/trim visual que não move o eixo analítico). Por isso as
        /// extremidades vêm da FACE PLANA real mais próxima de cada ponta,
        /// mesma técnica já comprovada em <see cref="RealAlignmentService"/>
        /// ("Alinhar Real"): entre as faces planas cuja normal é ~paralela ao
        /// eixo, pega a mais próxima do ponto bruto. Funciona pra qualquer
        /// direção de eixo (horizontal ou vertical), sem casos especiais.
        /// </summary>
        public static bool TryGetEnds(FamilyInstance instance, out XYZ end0, out XYZ end1, out XYZ axisDir)
        {
            end0 = end1 = axisDir = null;

            Line axis;
            if (instance?.Location is LocationCurve lc && lc.Curve is Line line)
            {
                axis = line;
            }
            else if (instance?.Location is LocationPoint lp)
            {
                var bb = instance.get_BoundingBox(null);
                if (bb == null) return false;
                // Eixo vertical nominal a partir do ponto de inserção — só serve
                // de ponto de partida pra achar as faces reais; o comprimento
                // exato não importa (a interseção reta-plano trata como infinita).
                axis = Line.CreateBound(
                    new XYZ(lp.Point.X, lp.Point.Y, bb.Min.Z),
                    new XYZ(lp.Point.X, lp.Point.Y, bb.Max.Z));
            }
            else
            {
                return false;
            }

            var dir = axis.GetEndPoint(1) - axis.GetEndPoint(0);
            if (dir.GetLength() < 1e-6) return false;
            axisDir = dir.Normalize();

            end0 = ResolveRealEnd(instance, axis, axis.GetEndPoint(0));
            end1 = ResolveRealEnd(instance, axis, axis.GetEndPoint(1));
            return true;
        }

        private static XYZ ResolveRealEnd(FamilyInstance instance, Line axis, XYZ roughEnd)
        {
            var face = RealAlignmentService.FindBestStopFace(instance, axis, roughEnd);
            if (face == null) return roughEnd;

            var plane = Plane.CreateByNormalAndOrigin(face.FaceNormal, face.Origin);
            return RealAlignmentService.IntersectLineWithPlane(axis, plane) ?? roughEnd;
        }

        /// <summary>
        /// Recentra o ponto no centro real da seção transversal — não confia em
        /// Justificação y/z nem em bounding box do elemento inteiro (afetada
        /// por cortes de conexão). Acha, em cada direção perpendicular ao eixo,
        /// UMA face real do lado negativo (mesma técnica de <see cref="ResolveRealEnd"/>,
        /// só que buscando na direção da largura/altura em vez do eixo) e anda
        /// meia dimensão CONHECIDA (lida do tipo do perfil) pra dentro — exatamente
        /// a ideia de achar a posição real por metade da altura/largura do perfil,
        /// só que partindo de uma face de verdade em vez de confiar na linha/ponto
        /// de desenho ou na bounding box do elemento inteiro.
        /// </summary>
        public static XYZ RecenterOnCrossSection(FamilyInstance instance, XYZ point,
                                                  XYZ widthDir, double widthMm,
                                                  XYZ heightDir, double heightMm)
        {
            var centered = RecenterAlong(instance, point, widthDir, widthMm) ?? point;
            centered = RecenterAlong(instance, centered, heightDir, heightMm) ?? centered;
            return centered;
        }

        private static XYZ RecenterAlong(FamilyInstance instance, XYZ point, XYZ dir, double sizeMm)
        {
            if (dir == null || sizeMm <= 0) return null;

            double halfFt = sizeMm / 304.8 / 2.0;
            double searchFt = sizeMm / 304.8 * 2.0 + 1.0; // além da metade real, garante alcançar a face de fora

            var searchStart = point - dir * searchFt;
            var searchLine = Line.CreateBound(searchStart, point + dir * searchFt);

            var face = RealAlignmentService.FindBestStopFace(instance, searchLine, searchStart);
            if (face == null) return null;

            var plane = Plane.CreateByNormalAndOrigin(face.FaceNormal, face.Origin);
            var facePoint = RealAlignmentService.IntersectLineWithPlane(searchLine, plane);
            if (facePoint == null) return null;

            // A face achada é a do lado onde a busca começou (mais perto de
            // searchStart) — o centro real fica meia dimensão pra dentro, no
            // sentido +dir.
            return facePoint + dir * halfFt;
        }

        public static bool TryGetCrossSectionFrame(FamilyInstance instance, XYZ axisDir,
                                                    out XYZ widthDir, out XYZ heightDir)
        {
            widthDir = heightDir = null;
            bool nearVertical = Math.Abs(axisDir.DotProduct(XYZ.BasisZ)) > 0.99;

            if (nearVertical)
            {
                // Eixo já é a vertical — a técnica de projetar BasisZ do mundo é
                // degenerada aqui. Usa a rotação real da instância.
                var t = instance.GetTransform();
                var rawWidth = t.BasisX - axisDir * axisDir.DotProduct(t.BasisX);
                if (rawWidth.GetLength() < 1e-6) return false;
                widthDir  = rawWidth.Normalize();
                heightDir = axisDir.CrossProduct(widthDir).Normalize();
                return true;
            }

            var upRaw = XYZ.BasisZ - axisDir * axisDir.DotProduct(XYZ.BasisZ);
            if (upRaw.GetLength() < 1e-6) return false;
            heightDir = upRaw.Normalize();
            widthDir  = heightDir.CrossProduct(axisDir).Normalize();
            return true;
        }
    }
}
