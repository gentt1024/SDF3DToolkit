
# SDF3DToolkit

SDF3DToolkit is a Unity toolkit for working with Signed Distance Fields (SDFs) in 3D space. This toolkit provides utilities for creating, manipulating, and querying SDFs, which are useful for various applications such as procedural content generation, physics simulations, and more.

## Features

- **SdfNode Class**: Represents a node in the SDF structure, providing methods for setting parameters, copying nodes, and calculating bounds and intersections.
- **Distance Field Management**: Handles the creation and release of render textures used as distance fields.
- **Voxel Data Handling**: Manages voxel data for distance fields and provides methods for querying signed distances and gradients.

## Installation

1. Clone or download the repository.
2. Copy the `SDF3DToolkit` folder into your Unity project's assets folder.

## Usage

### Creating an SdfNode

To create an `SdfNode`, you need a `RenderTexture`, a transformation matrix, and a voxel size:


```csharp
RenderTexture distanceField = new RenderTexture(width, height, depth);
Matrix4x4 matrix = Matrix4x4.identity;
float voxelSize = 1.0f;

SdfNode sdfNode = new SdfNode(distanceField, matrix, voxelSize);
```

### Copying an SdfNode

You can create a copy of an existing `SdfNode`:

```csharp
SdfNode copy = sdfNode.Copy();
```

### Calculating Bounds

You can get the local and world bounds of an `SdfNode`:

```csharp
Bounds localBounds = sdfNode.GetLocalBounds();
Bounds worldBounds = sdfNode.GetWorldBounds();
```

### Checking for Intersections

To check if two `SdfNode` instances intersect and get the intersection bounds:

```csharp
SdfNode otherNode = new SdfNode(otherDistanceField, otherMatrix, otherVoxelSize);
if (sdfNode.IntersectsBounds(otherNode, out Bounds intersectionBounds))
{
    // Intersection detected
}
```

### Querying Signed Distance

To get the signed distance at a specific world position:

```csharp
Vector3 worldPos = new Vector3(1.0f, 2.0f, 3.0f);
float signedDistance = sdfNode.SD(worldPos);
```

### Calculating Gradient

To calculate the gradient of the signed distance field at a specific world position:

```csharp
Vector3 gradient = sdfNode.Gradient(worldPos);
```

## License

This project is licensed under the MIT License. See the LICENSE file for details.

## Contributing

Contributions are welcome! Please open an issue or submit a pull request.