using UnityEngine;
using System;
using System.Collections.Generic;

namespace Astro.Engine
{
    public static class ShapeOptimizationHelper
    {

        // c# implementation of the Ramer-Douglas-Peucker-Algorithm by Craig Selbert slightly adapted for Unity Vector Types
        public static void OptimizePolygonCollider2D(PolygonCollider2D collider, double tolerance)
        {
            Vector2[] points = collider.points;
            List<Vector2> optimizedPoints = DouglasPeuckerReduction(new List<Vector2>(points), tolerance);
            collider.points = optimizedPoints.ToArray();
        }

        public static List<Vector2> DouglasPeuckerReduction(List<Vector2> points, double tolerance)
        {
            if (points == null || points.Count < 3)
                return points;

            int firstPoint = 0;
            int lastPoint = points.Count - 1;
            List<int> pointIndexsToKeep = new List<int>();

            //Add the first and last index to the keepers
            pointIndexsToKeep.Add(firstPoint);
            pointIndexsToKeep.Add(lastPoint);

            //The first and the last point cannot be the same
            while (points[firstPoint].Equals(points[lastPoint]))
            {
                lastPoint--;
            }

            DouglasPeuckerReductionRecursive(points, firstPoint, lastPoint, tolerance, ref pointIndexsToKeep);

            List<Vector2> returnPoints = new List<Vector2>();
            pointIndexsToKeep.Sort();
            foreach (int index in pointIndexsToKeep)
            {
                returnPoints.Add(points[index]);
            }

            return returnPoints;
        }

        private static void DouglasPeuckerReductionRecursive(List<Vector2> points, int firstPoint, int lastPoint, double tolerance, ref List<int> pointIndexsToKeep)
        {
            double maxDistance = 0;
            int indexFarthest = 0;

            for (int index = firstPoint; index < lastPoint; index++)
            {
                double distance = PerpendicularDistance(points[firstPoint], points[lastPoint], points[index]);
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    indexFarthest = index;
                }
            }

            if (maxDistance > tolerance && indexFarthest != 0)
            {
                //Add the largest point that exceeds the tolerance
                pointIndexsToKeep.Add(indexFarthest);

                DouglasPeuckerReductionRecursive(points, firstPoint, indexFarthest, tolerance, ref pointIndexsToKeep);
                DouglasPeuckerReductionRecursive(points, indexFarthest, lastPoint, tolerance, ref pointIndexsToKeep);
            }
        }

        private static double PerpendicularDistance(Vector2 point1, Vector2 point2, Vector2 point)
        {
            double area = Math.Abs(.5f * (point1.x * point2.y + point2.x * point.y + point.x * point1.y - point2.x * point1.y - point.x * point2.y - point1.x * point.y));
            double bottom = Math.Sqrt(Mathf.Pow(point1.x - point2.x, 2f) + Math.Pow(point1.y - point2.y, 2f));
            double height = area / bottom * 2f;

            return height;
        }
    }
}
