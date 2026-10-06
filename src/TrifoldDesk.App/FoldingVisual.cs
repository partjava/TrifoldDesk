using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace TrifoldDesk;

public sealed record FoldingProjection(Viewport3D Viewport, AxisAngleRotation3D Rotation);

public static class FoldingVisual
{
    /// <summary>Builds an animation-only textured plane. The actual pane is restored after animation.</summary>
    public static FoldingProjection Create(BitmapSource snapshot, bool axisDirection)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.PixelWidth < 1 || snapshot.PixelHeight < 1) throw new ArgumentException("折叠快照为空。", nameof(snapshot));
        var image = snapshot.IsFrozen ? snapshot : snapshot.Clone(); if (!image.IsFrozen) image.Freeze();
        double aspect = Math.Max(.001, snapshot.Width / Math.Max(1, snapshot.Height));
        double halfHeight = 1 / aspect;
        var mesh = new MeshGeometry3D {
            Positions = new Point3DCollection { new(-1, -halfHeight, 0), new(1, -halfHeight, 0), new(1, halfHeight, 0), new(-1, halfHeight, 0) },
            TextureCoordinates = new PointCollection { new(0, 1), new(1, 1), new(1, 0), new(0, 0) },
            TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 },
            Normals = new Vector3DCollection { new(0, 0, 1), new(0, 0, 1), new(0, 0, 1), new(0, 0, 1) }
        }; mesh.Freeze();
        var brush = new ImageBrush(image) { Stretch = Stretch.Fill }; brush.Freeze();
        var material = new EmissiveMaterial(brush); material.Freeze();
        var rotation = new AxisAngleRotation3D(new Vector3D(0, axisDirection ? -1 : 1, 0), 0);
        var transform = new RotateTransform3D(rotation, new Point3D(axisDirection ? 1 : -1, 0, 0));
        var face = new GeometryModel3D(mesh, material) { BackMaterial = material, Transform = transform };
        double distance = 1 / Math.Tan(Math.PI / 8);
        var viewport = new Viewport3D { Width = snapshot.Width, Height = snapshot.Height, IsHitTestVisible = false, ClipToBounds = true,
            Camera = new PerspectiveCamera(new Point3D(0, 0, distance), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 45) { NearPlaneDistance = .01, FarPlaneDistance = 100 } };
        viewport.Children.Add(new ModelVisual3D { Content = face });
        return new(viewport, rotation);
    }
}
