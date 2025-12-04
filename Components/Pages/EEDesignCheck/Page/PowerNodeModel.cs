using Blazor.Diagrams.Core.Models;
// 使用別名 'Geometry' 來明確區分，避免與 MAUI 的 Point/Size 衝突
using Geometry = Blazor.Diagrams.Core.Geometry;

namespace HWDPortalMaui.Components.Pages.EEDesignCheck
{
    public class PowerNodeModel : NodeModel
    {
        // 使用 Geometry.Point 明確指定型別
        public PowerNodeModel(Geometry.Point? position = null) : base(position)
        {
            // 使用 Geometry.Size 明確指定型別
            Size = new Geometry.Size(40, 40);
            // [移除] Class = "power-node"; // NodeModel 沒有此屬性，改由 CSS :has 處理
        }

        // 使用 Geometry.Point 明確指定型別
        public PowerNodeModel(string id, Geometry.Point? position = null) : base(id, position)
        {
            Size = new Geometry.Size(40, 40);
            // [移除] Class = "power-node"; // NodeModel 沒有此屬性，改由 CSS :has 處理
        }

        // 使用 Geometry.IShape 明確指定型別
        public override Geometry.IShape GetShape()
        {
            // 使用 Geometry.Shapes
            return Geometry.Shapes.Circle(this);
        }
    }
}