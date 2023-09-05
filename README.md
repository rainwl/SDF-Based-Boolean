# SDF-Based Boolean

## Approach

### I.Set Operations between SDFs
Because there's 2 sdf,static one and dynamic another,when dynamic one moving on,and when do union or diff operations,
the grid point between the nearest distance, we can do an inverse of transformation for the point and compute the distance.



### II.Level Set evolve boundary '0'

#### reference

`C:\Users\22153\Documents\Repository\imstk-unity\ImstkSource~\iMSTK\Source\DynamicalModels\ObjectModels\imstkLevelSetModel.cpp`

https://imstk.gitlab.io/Dynamical_Models/LevelSetModel.html

## Issues

- [ ] what is sdf , study sdf system
- [ ] calculate for sdf
- [ ] sdf in unity , shader ,compute shader ,C#
- [ ] type of sdf:render asset , 3D assets , etc.



## Knowledge

### SDF type
- Texture3D
- render Texture
- voxel (customized)
### Which type is suitable for mesh generation?


### Usage
- C# 
- Shader
- Compute Shader
- hybrid

## GitHub Repositories

### ISO Mesh
https://github.com/EmmetOT/IsoMesh.git



## Reference

### Signed Distance Function
In mathematics and its applications, the signed distance function (or oriented distance function) is the orthogonal distance of a given point x to the boundary of a set Ω in a metric space, with the sign determined by whether or not x is in the interior of Ω. The function has positive values at points x inside Ω, it decreases in value as x approaches the boundary of Ω where the signed distance function is zero, and it takes negative values outside of Ω.[1] However, the alternative convention is also sometimes taken instead (i.e., negative inside Ω and positive outside).

在数学及其应用中，有符号距离函数（或定向距离函数）是指在度量空间中给定点x到集合Ω的边界的正交距离，其符号由x是否位于Ω的内部确定。该函数在Ω内部的点x处具有正值，在x接近有符号距离函数为零的Ω边界时其值减小，并在Ω之外的地方取负值。然而，有时也会采用另一种约定（即在Ω内部为负，在Ω外部为正）。

`Intersect`
```c++
float intersectSDF(float distA,float distB){
    return max(distA,distB);
}
```
`Union`
```c++
float intersectSDF(float distA,float distB){
    return min(distA,distB);
}
```
`Difference`
```c++
float intersectSDF(float distA,float distB){
    return max(distA,-distB);
}
```

### sdf in unity (openai)

**表现形式：**

1. **纹理（Texture）SDF**：SDF可以存储在二维纹理中，其中每个像素代表了距离最近表面的距离值。

2. **体素（Voxel）SDF**：SDF可以存储在三维体素网格中，其中每个体素代表了距离最近表面的距离值。这种表现形式通常用于体积建模和物理模拟。

3. **几何体（Geometry）SDF**：SDF可以表示为几何体的隐式方程，例如球、圆柱、立方体等。这种表示形式通常用于建模和生成基本几何体。

**操作形式：**

4. **融合（Blending）**：将两个SDF图形以某种权重混合在一起，创建一个具有平滑过渡的新SDF图形。这通常用于创建柔和的过渡效果。

5. **变换和变形（Transformation and Deformation）**：对SDF进行平移、旋转、缩放和形状扭曲等变换操作，以创建复杂的变形效果。

6. **纹理映射（Texture Mapping）**：将SDF映射到几何体表面，以实现纹理贴图、材质映射和渲染效果。

Shader Graph、Compute Shader、Unity的Builtin Pipeline或Universal Render Pipeline（URP）


4. **渲染SDF模型：** 为了在场景中可视化SDF模型，你可以编写自定义的着色器或使用Shader Graph来创建SDF渲染效果。通常，SDF渲染器会根据SDF值来计算法线和表面特征，并使用这些信息来渲染模型。

5. **碰撞检测和物理模拟：** 你可以使用SDF来执行碰撞检测和物理模拟。Unity提供了一些物理引擎，如PhysX，可以与SDF一起使用，以模拟物体之间的碰撞和物理行为。



要在Unity中表示和处理一个三维模型的Signed Distance Field（SDF），通常需要以下步骤：

1. **生成SDF：** 生成三维模型的SDF数据。这可以通过不同的方法实现，包括体素化、光线投射等。你可以使用专门的SDF生成工具，如Volumetric Scene Representation (VSR)、Mesh2SDF等。

2. **存储SDF数据：** 生成的SDF数据需要以某种方式存储。最常见的方法是将SDF数据存储为三维纹理（3D Texture）或体素网格（Voxel Grid）。这可以使用Unity的Texture3D或自定义数据结构来实现。

3. **加载SDF数据：** 将SDF数据加载到Unity中。你可以将SDF数据存储为自定义Asset，并使用Unity的Asset Pipeline加载它们，或者在运行时通过编程方式加载SDF数据。

4. **渲染SDF模型：** 为了在场景中可视化SDF模型，你需要编写自定义的着色器或使用Shader Graph来创建SDF渲染效果。这包括根据SDF值计算法线和渲染模型表面。一种常见的方法是使用Marching Cubes或Marching Tetrahedra算法将SDF数据转换为可渲染的三维几何体。

5. **碰撞检测和物理模拟：** 你可以使用SDF来执行碰撞检测和物理模拟。Unity的Physics API可以与SDF一起使用，以模拟物体之间的碰撞和物理行为。你可以使用Physics.ComputePenetration来检测碰撞并解决碰撞。

6. **变换和变形：** 如果需要对SDF模型进行变换或变形，可以在渲染之前修改SDF数据。这可以用于实现动画效果或实时变形。

7. **编辑工具：** 如果需要在Unity中编辑SDF模型，你可以开发自定义的编辑工具，以便用户能够创建、修改和保存SDF数据。


#### Unity如何修改sdf
在Unity中修改Signed Distance Field（SDF）通常需要遵循以下步骤：

1. **获取SDF数据：** 首先，你需要获得要修改的SDF数据。这可以是纹理、体素网格或其他数据结构，具体取决于你的SDF表示方法。

2. **修改SDF数据：** 根据你的需求，你可以在SDF数据上执行不同的修改操作。以下是一些可能的修改操作：

    - **手动编辑：** 如果你需要手动编辑SDF，你可以遍历SDF数据的每个元素，并根据需要修改距离值。例如，你可以添加、减少或调整特定区域的距离值。

    - **运算操作：** 你可以使用运算操作来修改SDF。例如，要将两个SDF合并（并集操作），你可以对两个SDF数据的相应元素执行最小值操作。要执行剪切（补集操作），你可以对一个SDF减去另一个SDF的值。

    - **变换操作：** 如果需要对SDF进行变换（例如旋转、平移、缩放），你可以修改SDF数据以反映这些变换。这通常涉及到对SDF数据中的位置进行适当的变换。

3. **更新渲染或碰撞检测：** 如果你修改了SDF数据，并且需要在渲染或碰撞检测中反映这些修改，你需要相应地更新渲染或碰撞检测的流程。

    - **渲染：** 如果你修改了SDF数据，你需要使用修改后的SDF数据重新渲染SDF模型。这可能涉及到更新材质或重新计算几何体的表面。

    - **碰撞检测：** 如果你使用SDF进行碰撞检测，修改后的SDF数据会影响碰撞检测的结果。确保在碰撞检测之前使用最新的SDF数据进行检测。


