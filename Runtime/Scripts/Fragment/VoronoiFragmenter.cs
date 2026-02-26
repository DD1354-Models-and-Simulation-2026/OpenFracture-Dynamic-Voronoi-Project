using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using GK;

#if UNITY_EDITOR
using UnityEditor;
#endif

public static class VoronoiFragmenter
{
    /// <summary>
    /// Generates the mesh fragments based on the provided options. The generated fragment objects are
    /// stored as children of `fragmentParent`
    /// </summary>
    /// <param name="sourceObject">The source object to fragment. This object must have a MeshFilter, a RigidBody and a Collider.</param>
    /// <param name="options">Options for the fragmenter</param>
    /// <param name="fragmentTemplate">The template GameObject that each fragment will clone</param>
    /// <param name="parent">The parent transform for the fragment objects</param>
    /// <param name="saveToDisk">If true, the generated fragment meshes will be saved to disk so they can be re-used in prefabs.</param>
    /// <param name="saveFolderPath">The save location for the fragments.</param>
    /// <returns></returns>
    public static void Fracture(GameObject sourceObject,
                                FractureOptions options,
                                GameObject fragmentTemplate,
                                Transform parent,
                                bool saveToDisk = false,
                                string saveFolderPath = "")
    {
        // Define our source mesh data for the fracturing
        FragmentData sourceMesh = new FragmentData(sourceObject.GetComponent<MeshFilter>().sharedMesh);
        sourceMesh.CalculateBounds();
        
        // Generate Voronoi-based fragments
        var fragments = GenerateVoronoiFragments(sourceMesh, options);

        int i = 0;
        foreach(FragmentData meshData in fragments)
        {
            CreateFragment(meshData, 
                           sourceObject,
                           fragmentTemplate, 
                           parent,
                           saveToDisk,
                           saveFolderPath,
                           options.detectFloatingFragments,
                           ref i);
        }
    }

    /// <summary>
    /// Asynchronously generates the mesh fragments based on the provided options. The generated fragment objects are
    /// stored as children of `fragmentParent`
    /// </summary>
    /// <param name="sourceObject">The source object to fragment. This object must have a MeshFilter, a RigidBody and a Collider.</param>
    /// <param name="options">Options for the fragmenter</param>
    /// <param name="fragmentTemplate">The template GameObject that each fragment will clone</param>
    /// <param name="parent">The parent transform for the fragment objects</param>
    /// <returns></returns>
    public static IEnumerator FractureAsync(GameObject sourceObject,
                                            FractureOptions options,
                                            GameObject fragmentTemplate,
                                            Transform parent,
                                            Action onCompletion)
    {
        // Define our source mesh data for the fracturing
        FragmentData sourceMesh = new FragmentData(sourceObject.GetComponent<MeshFilter>().sharedMesh);
        sourceMesh.CalculateBounds();
        
        // Generate Voronoi-based fragments asynchronously
        var fragments = new List<FragmentData>();
        yield return GenerateVoronoiFragmentsAsync(sourceMesh, options, fragments);

        int i = 0;
        foreach(FragmentData meshData in fragments)
        {
            CreateFragment(meshData, 
                           sourceObject,
                           fragmentTemplate, 
                           parent,
                           false,
                           "",
                           options.detectFloatingFragments,
                           ref i);
        }

        onCompletion?.Invoke();
    }

    /// <summary>
    /// Generates the mesh fragments based on the provided options. The generated fragment objects are
    /// stored as children of `fragmentParent`
    /// </summary>
    /// <param name="sourceObject">The source object to slice. This object must have a MeshFilter, a RigidBody and a Collider.</param>
    /// <param name="sliceNormal">The normal of the cut plane in the local frame of sourceObject.</param>
    /// <param name="sliceOrigin">The origin of the cut plane in the local frame of sourceObject.</param>
    /// <param name="options">Options for the slicer</param>
    /// <param name="fragmentTemplate">The template GameObject that each slice will clone</param>
    /// <param name="parent">The parent transform for the fragment objects</param>
    /// <returns></returns>
    public static void Slice(GameObject sourceObject,
                             Vector3 sliceNormal,
                             Vector3 sliceOrigin,
                             SliceOptions options,
                             GameObject fragmentTemplate,
                             Transform parent)
    {
        // Define our source mesh data for the fracturing
        FragmentData sourceMesh = new FragmentData(sourceObject.GetComponent<MeshFilter>().sharedMesh);
        // Subdivide the mesh into multiple fragments until we reach the fragment limit
        FragmentData topSlice, bottomSlice;

        // Slice and dice!
        MeshSlicer.Slice(sourceMesh,
                         sliceNormal,
                         sliceOrigin,
                         options.textureScale,
                         options.textureOffset,
                         out topSlice,
                         out bottomSlice);

        int i = 0;
        CreateFragment(topSlice,
                       sourceObject,
                       fragmentTemplate,
                       parent,
                       false,
                       "",
                       options.detectFloatingFragments,
                       ref i);

        CreateFragment(bottomSlice,
                       sourceObject,
                       fragmentTemplate,
                       parent,
                       false,
                       "",
                       options.detectFloatingFragments,
                       ref i);
    }

    /// <summary>
    /// Creates a new GameObject from the fragment data
    /// </summary>
    /// <param name="fragmentMeshData">Geometry of the fragment produced by the slicer</param>
    /// <param name="sourceObject">The source object to fragment. This object must have a MeshFilter, a RigidBody and a Collider.</param>
    /// <param name="fragmentTemplate">The template GameObject that each fragment will clone</param>
    /// <param name="parent">The parent transform for the fragment objects</param>
    /// <param name="i">Fragment counter</param>
    private static void CreateFragment(FragmentData fragmentMeshData,
                                       GameObject sourceObject,
                                       GameObject fragmentTemplate,
                                       Transform parent,
                                       bool saveToDisk,
                                       string saveFolderPath,
                                       bool detectFloatingFragments,
                                       ref int i)
    {
        // If there is no mesh data, don't create an object
        if (fragmentMeshData.Triangles.Length == 0)
        {
            return;
        }

        Mesh[] meshes;
        Mesh fragmentMesh = fragmentMeshData.ToMesh();

        // If the "Detect Floating Fragments" option is enabled, take the fragment mesh and
        // identify disconnected sets of geometry within it, treating each of these as a
        // separate physical object
        if (detectFloatingFragments)
        {
            meshes = MeshUtils.FindDisconnectedMeshes(fragmentMesh);
        }
        else
        {
            meshes = new Mesh[] { fragmentMesh };
        }

        var parentSize = sourceObject.GetComponent<MeshFilter>().sharedMesh.bounds.size;
        var parentMass = sourceObject.GetComponent<Rigidbody>().mass;

        for(int k = 0; k < meshes.Length; k++)
        {
            GameObject fragment = GameObject.Instantiate(fragmentTemplate, parent);
            fragment.name = $"Fragment{i}";
            fragment.transform.localPosition = Vector3.zero;
            fragment.transform.localRotation = Quaternion.identity;
            fragment.transform.localScale = sourceObject.transform.localScale;

            meshes[k].name = System.Guid.NewGuid().ToString();

            // Update mesh to the new sliced mesh
            var meshFilter = fragment.GetComponent<MeshFilter>();
            meshFilter.sharedMesh = meshes[k];

            var collider = fragment.GetComponent<MeshCollider>();

            // If fragment collisions are disabled, collider will be null
            collider.sharedMesh = meshes[k];
            collider.convex = true;
            collider.sharedMaterial = fragment.GetComponent<Collider>().sharedMaterial;

            // Compute mass of the sliced object by dividing mesh bounds by density
            var parentRigidBody = sourceObject.GetComponent<Rigidbody>();
            var rigidBody = fragment.GetComponent<Rigidbody>();

            var size = fragmentMesh.bounds.size;
            float density = (parentSize.x * parentSize.y * parentSize.z) / parentMass;
            rigidBody.mass = (size.x * size.y * size.z) / density;
            
            // This code only compiles for the editor
            #if UNITY_EDITOR
            if (saveToDisk)
            {
                string path = $"{saveFolderPath}/{meshes[k].name}.asset";
                AssetDatabase.CreateAsset(meshes[k], path);
            }
            #endif

            i++;
        }
    }

    /// <summary>
    /// Generate Voronoi-based fragments from source mesh
    /// </summary>
    private static List<FragmentData> GenerateVoronoiFragments(FragmentData sourceMesh, FractureOptions options)
    {
        var result = new List<FragmentData>();
        
        // Generate 3D Voronoi sites
        Vector3[] sites3D = GenerateVoronoiSites(sourceMesh.Bounds, options.impactPoint, options.fragmentCount);
        
        // Determine projection plane based on impact direction
        Vector3 impactDir = options.impactDirection;

        impactDir = impactDir.normalized;
        Vector3 planeRight, planeUp;
        DetermineProjectionPlane(impactDir, out planeRight, out planeUp);
        
        // Project sites to 2D
        Vector3 projectionCenter = sourceMesh.Bounds.center;
        Vector2[] sites2D = new Vector2[sites3D.Length];
        for (int i = 0; i < sites3D.Length; i++)
        {
            sites2D[i] = ProjectTo2D(sites3D[i], projectionCenter, planeRight, planeUp);
        }
        
        // Calculate 2D Voronoi diagram
        var voronoiCalc = new VoronoiCalculator();
        var voronoiDiagram = voronoiCalc.CalculateDiagram(sites2D);
        
        // Create 2D bounding polygon for clipping
        Vector2[] boundingPolygon = CreateBoundingPolygon2D(sourceMesh.Bounds, projectionCenter, planeRight, planeUp);
        
        // Process each Voronoi cell
        var voronoiClipper = new VoronoiClipper();
        for (int siteIndex = 0; siteIndex < sites2D.Length; siteIndex++)
        {
            var clippedCell2D = new List<Vector2>();
            voronoiClipper.ClipSite(voronoiDiagram, boundingPolygon, siteIndex, ref clippedCell2D);
            
            if (clippedCell2D.Count < 3)
                continue; // Skip degenerate cells
            
            // Convert 2D cell to 3D cutting planes
            FragmentData cellFragment = SliceMeshByVoronoiCell(
                sourceMesh, 
                clippedCell2D, 
                projectionCenter, 
                planeRight, 
                planeUp, 
                impactDir,
                options);
            
            if (cellFragment != null && cellFragment.Triangles.Length > 0)
            {
                result.Add(cellFragment);
            }
            else
            {
                Debug.LogWarning($"[VoronoiFragmenter] Cell {siteIndex} produced empty fragment.");
            }
        }

        Debug.Log($"[VoronoiFragmenter] Fracture complete: fragments={result.Count}");
        
        return result;
    }

    /// <summary>
    /// Generate Voronoi-based fragments asynchronously
    /// </summary>
    private static IEnumerator GenerateVoronoiFragmentsAsync(FragmentData sourceMesh, FractureOptions options, List<FragmentData> result)
    {
        Debug.Log($"[VoronoiFragmenter] Start async fracture: vertices={sourceMesh.vertexCount}, triangles={sourceMesh.triangleCount}, fragments={options.fragmentCount}");
        // Generate 3D Voronoi sites
        Vector3[] sites3D = GenerateVoronoiSites(sourceMesh.Bounds, options.impactPoint, options.fragmentCount);
        
        // Determine projection plane based on impact direction
        Vector3 impactDir = options.impactDirection;
        if (impactDir.sqrMagnitude < 0.0001f)
        {
            Debug.LogWarning("[VoronoiFragmenter] impactDirection too small, defaulting to Vector3.up");
            impactDir = Vector3.up;
        }
        impactDir = impactDir.normalized;
        Vector3 planeRight, planeUp;
        DetermineProjectionPlane(impactDir, out planeRight, out planeUp);
        
        // Project sites to 2D
        Vector3 projectionCenter = sourceMesh.Bounds.center;
        Vector2[] sites2D = new Vector2[sites3D.Length];
        for (int i = 0; i < sites3D.Length; i++)
        {
            sites2D[i] = ProjectTo2D(sites3D[i], projectionCenter, planeRight, planeUp);
        }
        
        // Calculate 2D Voronoi diagram
        var voronoiCalc = new VoronoiCalculator();
        var voronoiDiagram = voronoiCalc.CalculateDiagram(sites2D);
        Debug.Log($"[VoronoiFragmenter] Voronoi sites={sites2D.Length}, edges={voronoiDiagram.Edges.Count}");
        
        yield return null;
        
        // Create 2D bounding polygon for clipping
        Vector2[] boundingPolygon = CreateBoundingPolygon2D(sourceMesh.Bounds, projectionCenter, planeRight, planeUp);
        
        // Process each Voronoi cell
        var voronoiClipper = new VoronoiClipper();
        for (int siteIndex = 0; siteIndex < sites2D.Length; siteIndex++)
        {
            var clippedCell2D = new List<Vector2>();
            voronoiClipper.ClipSite(voronoiDiagram, boundingPolygon, siteIndex, ref clippedCell2D);
            
            if (clippedCell2D.Count < 3)
                continue;
            
            // Convert 2D cell to 3D cutting planes
            FragmentData cellFragment = SliceMeshByVoronoiCell(
                sourceMesh, 
                clippedCell2D, 
                projectionCenter, 
                planeRight, 
                planeUp, 
                impactDir,
                options);
            
            if (cellFragment != null && cellFragment.Triangles.Length > 0)
            {
                result.Add(cellFragment);
            }
            else
            {
                Debug.LogWarning($"[VoronoiFragmenter] Cell {siteIndex} produced empty fragment.");
            }
            
            // Yield every few cells to maintain responsiveness
            if (siteIndex % 3 == 0)
                yield return null;
        }

        Debug.Log($"[VoronoiFragmenter] Async fracture complete: fragments={result.Count}");
    }

    /// <summary>
    /// Generate random Voronoi sites around impact point
    /// </summary>
    private static Vector3[] GenerateVoronoiSites(Bounds bounds, Vector3 impactPoint, int count)
    {
        Vector3[] sites = new Vector3[count];
        float avgRadius = (bounds.size.x + bounds.size.y + bounds.size.z) / 6f;
        
        for (int i = 0; i < count; i++)
        {
            // Use Gaussian distribution around impact point (similar to BreakableSurface)
            float dist = Mathf.Abs(NormalizedRandom(avgRadius * 0.5f, avgRadius * 0.3f));
            
            // Random direction in 3D
            float theta = Random.Range(0f, Mathf.PI * 2f);
            float phi = Random.Range(0f, Mathf.PI);
            
            Vector3 direction = new Vector3(
                Mathf.Sin(phi) * Mathf.Cos(theta),
                Mathf.Sin(phi) * Mathf.Sin(theta),
                Mathf.Cos(phi)
            );
            
            sites[i] = impactPoint + direction * dist;
        }
        
        return sites;
    }

    /// <summary>
    /// Gaussian random number generator (from BreakableSurface)
    /// </summary>
    private static float NormalizedRandom(float mean, float stddev)
    {
        var u1 = Random.value;
        var u2 = Random.value;
        
        var randStdNormal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.PI * u2);
        
        return mean + stddev * randStdNormal;
    }

    /// <summary>
    /// Determine projection plane basis vectors from impact direction
    /// </summary>
    private static void DetermineProjectionPlane(Vector3 impactDir, out Vector3 right, out Vector3 up)
    {
        // Choose a reference vector that's not parallel to impactDir
        Vector3 reference = Mathf.Abs(impactDir.y) < 0.9f ? Vector3.up : Vector3.right;
        
        // Create orthonormal basis
        right = Vector3.Cross(impactDir, reference).normalized;
        up = Vector3.Cross(right, impactDir).normalized;
    }

    /// <summary>
    /// Project 3D point to 2D plane
    /// </summary>
    private static Vector2 ProjectTo2D(Vector3 point3D, Vector3 planeOrigin, Vector3 planeRight, Vector3 planeUp)
    {
        Vector3 offset = point3D - planeOrigin;
        return new Vector2(
            Vector3.Dot(offset, planeRight),
            Vector3.Dot(offset, planeUp)
        );
    }

    /// <summary>
    /// Project 2D point back to 3D
    /// </summary>
    private static Vector3 ProjectTo3D(Vector2 point2D, Vector3 planeOrigin, Vector3 planeRight, Vector3 planeUp)
    {
        return planeOrigin + planeRight * point2D.x + planeUp * point2D.y;
    }

    /// <summary>
    /// Create 2D bounding polygon from 3D bounds
    /// </summary>
    private static Vector2[] CreateBoundingPolygon2D(Bounds bounds, Vector3 planeOrigin, Vector3 planeRight, Vector3 planeUp)
    {
        // Project 8 corners of the bounding box to 2D
        Vector3[] corners3D = new Vector3[8];
        corners3D[0] = bounds.min;
        corners3D[1] = new Vector3(bounds.max.x, bounds.min.y, bounds.min.z);
        corners3D[2] = new Vector3(bounds.max.x, bounds.max.y, bounds.min.z);
        corners3D[3] = new Vector3(bounds.min.x, bounds.max.y, bounds.min.z);
        corners3D[4] = new Vector3(bounds.min.x, bounds.min.y, bounds.max.z);
        corners3D[5] = new Vector3(bounds.max.x, bounds.min.y, bounds.max.z);
        corners3D[6] = bounds.max;
        corners3D[7] = new Vector3(bounds.min.x, bounds.max.y, bounds.max.z);
        
        Vector2[] corners2D = new Vector2[8];
        for (int i = 0; i < 8; i++)
        {
            corners2D[i] = ProjectTo2D(corners3D[i], planeOrigin, planeRight, planeUp);
        }
        
        // Find convex hull of projected points
        return ComputeConvexHull2D(corners2D);
    }

    /// <summary>
    /// Compute 2D convex hull using Graham scan
    /// </summary>
    private static Vector2[] ComputeConvexHull2D(Vector2[] points)
    {
        if (points.Length < 3)
            return points;
        
        // Find bottom-most point (or left-most if tie)
        int minIndex = 0;
        for (int i = 1; i < points.Length; i++)
        {
            if (points[i].y < points[minIndex].y || 
                (points[i].y == points[minIndex].y && points[i].x < points[minIndex].x))
            {
                minIndex = i;
            }
        }
        
        Vector2 pivot = points[minIndex];
        
        // Sort points by polar angle with respect to pivot
        List<Vector2> sortedPoints = new List<Vector2>(points);
        sortedPoints.RemoveAt(minIndex);
        sortedPoints.Sort((a, b) => {
            float angleA = Mathf.Atan2(a.y - pivot.y, a.x - pivot.x);
            float angleB = Mathf.Atan2(b.y - pivot.y, b.x - pivot.x);
            return angleA.CompareTo(angleB);
        });
        
        // Graham scan
        List<Vector2> hull = new List<Vector2>();
        hull.Add(pivot);
        
        foreach (var point in sortedPoints)
        {
            while (hull.Count > 1)
            {
                Vector2 top = hull[hull.Count - 1];
                Vector2 nextTop = hull[hull.Count - 2];
                
                // Check if we make a left turn
                float cross = (top.x - nextTop.x) * (point.y - nextTop.y) - 
                             (top.y - nextTop.y) * (point.x - nextTop.x);
                
                if (cross > 0)
                    break;
                    
                hull.RemoveAt(hull.Count - 1);
            }
            hull.Add(point);
        }
        
        return hull.ToArray();
    }

    /// <summary>
    /// Slice mesh by Voronoi cell using its edge planes
    /// </summary>
    private static FragmentData SliceMeshByVoronoiCell(
        FragmentData sourceMesh,
        List<Vector2> cell2D,
        Vector3 planeOrigin,
        Vector3 planeRight,
        Vector3 planeUp,
        Vector3 impactDir,
        FractureOptions options)
    {
        // Start with a copy of the source mesh
        FragmentData currentFragment = CopyFragmentData(sourceMesh);
        if (cell2D == null || cell2D.Count < 3)
        {
            Debug.LogWarning("[VoronoiFragmenter] Cell has insufficient vertices.");
            return null;
        }
        
        // For each edge of the 2D Voronoi cell, create a cutting plane
        for (int i = 0; i < cell2D.Count; i++)
        {
            int nextI = (i + 1) % cell2D.Count;
            
            Vector2 p0_2D = cell2D[i];
            Vector2 p1_2D = cell2D[nextI];
            
            // Convert edge to 3D
            Vector3 p0_3D = ProjectTo3D(p0_2D, planeOrigin, planeRight, planeUp);
            Vector3 p1_3D = ProjectTo3D(p1_2D, planeOrigin, planeRight, planeUp);
            
            // Edge direction in 3D (perpendicular to impact direction)
            Vector3 edgeDir = (p1_3D - p0_3D).normalized;
            
            // Cutting plane normal (perpendicular to edge and pointing inward)
            // Cross product of edge direction and impact direction
            Vector3 planeNormal = Vector3.Cross(edgeDir, impactDir).normalized;
            
            // Ensure normal points inward toward cell center
            Vector3 cellCenter3D = ProjectTo3D(GetCenter2D(cell2D), planeOrigin, planeRight, planeUp);
            Vector3 toCenter = cellCenter3D - p0_3D;
            if (Vector3.Dot(planeNormal, toCenter) < 0)
            {
                planeNormal = -planeNormal;
            }
            
            // Slice the fragment
            FragmentData topSlice, bottomSlice;
            MeshSlicer.Slice(currentFragment,
                           planeNormal,
                           p0_3D,
                           options.textureScale,
                           options.textureOffset,
                           out topSlice,
                           out bottomSlice);
            
            // Determine which slice is inside the cell
            // Check which side the cell center is on
            float centerDist = Vector3.Dot(cellCenter3D - p0_3D, planeNormal);
            currentFragment = centerDist >= 0 ? topSlice : bottomSlice;
            
            if (currentFragment.Triangles.Length == 0)
                break; // Fragment was completely cut away
        }
        
        return currentFragment;
    }

    /// <summary>
    /// Get center of 2D polygon
    /// </summary>
    private static Vector2 GetCenter2D(List<Vector2> polygon)
    {
        Vector2 sum = Vector2.zero;
        foreach (var p in polygon)
        {
            sum += p;
        }
        return sum / polygon.Count;
    }

    /// <summary>
    /// Create a copy of FragmentData
    /// </summary>
    private static FragmentData CopyFragmentData(FragmentData source)
    {
        var copy = new FragmentData(source.Vertices.Count, source.triangleCount);
        
        // Copy vertices
        copy.Vertices.Clear();
        copy.Vertices.AddRange(source.Vertices);
        copy.CutVertices.Clear();
        copy.CutVertices.AddRange(source.CutVertices);
        
        // Copy triangles
        for (int i = 0; i < source.Triangles.Length && i < copy.Triangles.Length; i++)
        {
            copy.Triangles[i].Clear();
            copy.Triangles[i].AddRange(source.Triangles[i]);
        }
        
        // Copy constraints
        copy.Constraints.Clear();
        copy.Constraints.AddRange(source.Constraints);
        
        // Copy index map
        if (source.IndexMap != null)
        {
            copy.IndexMap = new int[source.IndexMap.Length];
            System.Array.Copy(source.IndexMap, copy.IndexMap, source.IndexMap.Length);
        }
        
        copy.Bounds = source.Bounds;
        
        return copy;
    }
}