using Google.OrTools.ConstraintSolver;
using Google.Protobuf.WellKnownTypes;

namespace VibeTrade.Backend.Features.Routing.Services;

/// <summary>
/// Ruta única: inicio en 0, visitar todos los nodos 1..sink-1 exactamente una vez, terminar en sink (sumidero).
/// <c>costMatrix</c> debe tener coste bajo solo desde entregas hacia sink; el resto hacia sink muy alto.
/// <c>precedences</c> son aristas de orden: <c>Before</c> debe aparecer antes que <c>After</c>.
/// Portado tal cual del subsistema de rutas de referencia.
/// </summary>
public static class OpenEndedTourOptimizer
{
    public const string VisitOrderDimensionName = "VisitOrder";

    private static readonly int[] StartNodes = [0];

    public static bool TrySolve(
        long[,] costMatrix,
        int sinkIndex,
        out List<int> route,
        out long totalCost,
        IReadOnlyList<(int Before, int After)>? precedences = null)
    {
        route = [];
        totalCost = 0;
        var n = costMatrix.GetLength(0);
        if (n < 3 || sinkIndex != n - 1 || sinkIndex <= 0)
            return false;

        try
        {
            var manager = new RoutingIndexManager(n, 1, StartNodes, [sinkIndex]);
            var routing = new RoutingModel(manager);

            var transitIndex = routing.RegisterTransitCallback((long fromIndex, long toIndex) =>
            {
                var fromNode = manager.IndexToNode(fromIndex);
                var toNode = manager.IndexToNode(toIndex);
                return costMatrix[fromNode, toNode];
            });
            routing.SetArcCostEvaluatorOfAllVehicles(transitIndex);

            if (precedences is { Count: > 0 })
            {
                var dimPair = routing.AddConstantDimension(1, n + 50, true, VisitOrderDimensionName);
                if (!dimPair.second)
                    return false;
                var visitDim = routing.GetDimensionOrDie(VisitOrderDimensionName);
                foreach (var (before, after) in precedences)
                {
                    if (before <= 0 || before >= sinkIndex || after <= 0 || after >= sinkIndex)
                        continue;
                    var beforeIdx = manager.NodeToIndex(before);
                    var afterIdx = manager.NodeToIndex(after);
                    routing.solver().Add(visitDim.CumulVar(beforeIdx) < visitDim.CumulVar(afterIdx));
                }
            }

            var parameters = operations_research_constraint_solver.DefaultRoutingSearchParameters();
            parameters.FirstSolutionStrategy = FirstSolutionStrategy.Types.Value.PathCheapestArc;
            parameters.LocalSearchMetaheuristic = LocalSearchMetaheuristic.Types.Value.GuidedLocalSearch;
            parameters.TimeLimit = new Duration { Seconds = 20 };

            var solution = routing.SolveWithParameters(parameters);
            if (solution == null)
                return false;

            long index = routing.Start(0);
            while (!routing.IsEnd(index))
            {
                var node = manager.IndexToNode(index);
                route.Add(node);
                index = solution.Value(routing.NextVar(index));
            }

            route.Add(manager.IndexToNode(index));

            totalCost = solution.ObjectiveValue();
            if (route.Count < 3)
                return false;
            return precedences == null || precedences.Count == 0 || RouteRespectsPrecedences(route, precedences, sinkIndex);
        }
        catch
        {
            route = [];
            totalCost = 0;
            return false;
        }
    }

    private static bool RouteRespectsPrecedences(
        IReadOnlyList<int> route,
        IReadOnlyList<(int Before, int After)> precedences,
        int sinkIndex)
    {
        var pos = new Dictionary<int, int>(route.Count);
        for (var i = 0; i < route.Count; i++)
        {
            var node = route[i];
            if (node != sinkIndex)
                pos[node] = i;
        }

        foreach (var (before, after) in precedences)
        {
            if (!pos.TryGetValue(before, out var pb) || !pos.TryGetValue(after, out var pa))
                return false;
            if (pb >= pa)
                return false;
        }

        return true;
    }
}
