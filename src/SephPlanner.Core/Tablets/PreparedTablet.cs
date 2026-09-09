using System.Linq;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Tablets
{
    internal sealed class PreparedTablet
    {
        private readonly GridSpec _grid;
        private readonly int _rotation;
        private readonly string _query, _condition;

        internal readonly GridPos Position;
        internal readonly (GridPos Position, TabletEffectKind Kind, int Amount)[] Effects;
        internal readonly (GridPos Position, TabletCriteriaKind Kind)[] Criteria;

        internal PreparedTablet(TabletPlacement placement, GridSpec grid)
        {
            _grid = grid;
            _rotation = placement.Rotation;
            _query = placement.Query;
            _condition = placement.ConditionQuery;
            Position = placement.Position;
            Effects = TabletQuery.Parse(_query, grid, Position, _rotation).Select(cell =>
            {
                var (kind, amount) = QueryValue.ReadEffect(cell.Value);
                return (cell.Position, kind, amount);
            }).ToArray();
            Criteria = TabletQuery.Parse(_condition, grid, Position, _rotation)
                .Select(cell => (cell.Position, QueryValue.ReadCriteria(cell.Value))).ToArray();
        }

        internal bool Matches(TabletPlacement placement, GridSpec grid) =>
            _grid.Width == grid.Width && _grid.Height == grid.Height && _grid.Storage == grid.Storage &&
            Position == placement.Position && _rotation == placement.Rotation &&
            _query == placement.Query && _condition == placement.ConditionQuery;
    }
}
