using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Creates lightweight, coloured low-poly trees entirely from runtime meshes.
/// The scatter area is expressed in this component's local space, which makes
/// the generator GameObject a convenient placement anchor for a level.
/// </summary>
[DisallowMultipleComponent]
public sealed class LowPolyRuntimeTreeGenerator : MonoBehaviour
{
    // Conservative caps prevent accidental high-cost settings on WebGL/mobile.
    private const int MaxTreeCount = 200;
    private const int MaxPlacementAttempts = 100;
    private const int MaxTrunkSides = 8;
    private const int MaxCanopyBlobCount = 8;
    private const int MaxBranchCount = 6;
    private const int MaxMaterialVariants = 8;
    private const int MaterialSeedSalt = 1597463007;

    private struct CanopyInteractionBlob
    {
        public Vector3 LocalCentre;
        public Vector3 LocalRadii;
    }

    private sealed class TreeInteractionRecord
    {
        public Transform Root;
        public float TrunkHeight;
        public float TrunkRadius;
        public CanopyInteractionBlob[] CanopyBlobs;
    }

    [Header("Tree Population")]
    [Tooltip("Number of trees requested per generation pass.")]
    [SerializeField, Range(0, MaxTreeCount)] private int treeCount = 30;
    [Tooltip("Width of the local-space scatter rectangle.")]
    [SerializeField, Min(0f)] private float scatterWidth = 30f;
    [Tooltip("Depth of the local-space scatter rectangle.")]
    [SerializeField, Min(0f)] private float scatterDepth = 20f;
    [Tooltip("Local-space centre of the XZ scatter area. Y defaults to ground level zero.")]
    [SerializeField] private Vector3 scatterCentre = Vector3.zero;
    [Tooltip("When enabled, the integer seed gives repeatable trees. Disable it for a fresh result each generation.")]
    [SerializeField] private bool useRandomSeed = true;
    [Tooltip("Seed used for repeatable placement, geometry, colours, rotation, and scale.")]
    [SerializeField] private int randomSeed = 12345;
    [Tooltip("Minimum centre-to-centre distance between trees on the local XZ plane.")]
    [SerializeField, Min(0f)] private float minimumSpacing = 2.25f;
    [Tooltip("Stops placement from looping indefinitely when the area is crowded.")]
    [SerializeField, Range(1, MaxPlacementAttempts)] private int maxPlacementAttemptsPerTree = 30;

    [Header("Tree Transform Variation")]
    [Tooltip("Applies a random local Y rotation to each tree.")]
    [SerializeField] private bool randomRotation = true;
    [Tooltip("Applies one uniform random scale to each tree.")]
    [SerializeField] private bool randomScale = true;
    [SerializeField, Min(0.01f)] private float minimumTreeScale = 0.8f;
    [SerializeField, Min(0.01f)] private float maximumTreeScale = 1.3f;

    [Header("Trunk")]
    [Tooltip("X is the minimum and Y is the maximum.")]
    [SerializeField] private Vector2 trunkHeightRange = new Vector2(2.6f, 4.2f);
    [Tooltip("X is the minimum and Y is the maximum.")]
    [SerializeField] private Vector2 trunkRadiusRange = new Vector2(0.22f, 0.4f);
    [Tooltip("A random side count in this range is used for each trunk.")]
    [SerializeField] private Vector2Int trunkSideCountRange = new Vector2Int(5, 7);

    [Header("Canopy")]
    [Tooltip("Horizontal diameter range for each tree's canopy blobs.")]
    [SerializeField] private Vector2 canopySizeRange = new Vector2(2.1f, 3.4f);
    [Tooltip("Vertical height range for each tree's canopy blobs.")]
    [SerializeField] private Vector2 canopyHeightRange = new Vector2(1.8f, 2.8f);
    [Tooltip("More blobs make a fuller canopy but add polygons. Four is a good mobile default.")]
    [SerializeField, Range(1, MaxCanopyBlobCount)] private int canopyBlobsPerTree = 4;
    [Tooltip("Maximum horizontal offset of non-central canopy blobs.")]
    [SerializeField, Min(0f)] private float canopyBlobSpread = 0.9f;

    [Header("Branches")]
    [Tooltip("Set both values to zero to disable branches.")]
    [SerializeField] private Vector2Int branchCountRange = new Vector2Int(2, 4);
    [Tooltip("X is the minimum and Y is the maximum branch length.")]
    [SerializeField] private Vector2 branchLengthRange = new Vector2(0.7f, 1.35f);
    [Tooltip("X is the minimum and Y is the maximum branch base radius.")]
    [SerializeField] private Vector2 branchRadiusRange = new Vector2(0.07f, 0.14f);

    [Header("Materials")]
    [Tooltip("Uses valid assigned material assets instead of generated materials.")]
    [SerializeField] private bool useAssignedMaterials;
    [SerializeField] private Material assignedTrunkMaterial;
    [SerializeField] private Material assignedLeafMaterial;
    [Tooltip("Creates reusable unlit colour palettes when assigned materials are not used. Disable for one shared midpoint colour per part type.")]
    [SerializeField] private bool autoGenerateMaterials = true;
    [Tooltip("Number of shared trunk/leaf colour variants. This is a palette, not a material per tree.")]
    [SerializeField, Range(1, MaxMaterialVariants)] private int generatedMaterialVariants = 4;
    [SerializeField, ColorUsage(false, false)] private Color trunkColourA = new Color(0.32f, 0.15f, 0.055f, 1f);
    [SerializeField, ColorUsage(false, false)] private Color trunkColourB = new Color(0.56f, 0.3f, 0.1f, 1f);
    [SerializeField, ColorUsage(false, false)] private Color leafColourA = new Color(0.17f, 0.54f, 0.16f, 1f);
    [SerializeField, ColorUsage(false, false)] private Color leafColourB = new Color(0.44f, 0.8f, 0.22f, 1f);

    [Header("Rendering")]
    [Tooltip("Disabled by default for inexpensive WebGL rendering. Enable only if the scene needs tree shadows.")]
    [SerializeField] private bool castShadows;

    [Header("Generated Hierarchy")]
    [Tooltip("Groups generated tree roots beneath one empty child GameObject.")]
    [SerializeField] private bool parentGeneratedTrees = true;
    [SerializeField] private string generatedParentName = "Generated Low Poly Trees";
    [Tooltip("Recommended. Removes this generator's previous runtime trees before rebuilding.")]
    [SerializeField] private bool clearExistingTreesBeforeRegenerating = true;

    private readonly List<GameObject> generatedTreeRoots = new List<GameObject>();
    private readonly List<Vector3> generatedTreePositions = new List<Vector3>();
    private readonly List<Mesh> generatedMeshes = new List<Mesh>();
    private readonly List<Material> generatedMaterials = new List<Material>();
    private readonly List<Material> trunkMaterialPalette = new List<Material>();
    private readonly List<Material> leafMaterialPalette = new List<Material>();
    private readonly List<TreeInteractionRecord> treeInteractionRecords = new List<TreeInteractionRecord>();

    private GameObject generatedParent;
    private Shader cachedUnlitShader;
    private int trunkPaletteSignature;
    private int leafPaletteSignature;
    private int nextTreeIndex = 1;

    private void Start()
    {
        GenerateTrees();
    }

    /// <summary>
    /// Generates a new group of trees. This is public so it can be called by a
    /// runtime UI button, UnityEvent, or another gameplay script later.
    /// </summary>
    public void GenerateTrees()
    {
        ValidateSettings();

        if (clearExistingTreesBeforeRegenerating)
        {
            ClearTrees();
        }

        if (treeCount == 0)
        {
            return;
        }

        int seed = useRandomSeed
            ? randomSeed
            : unchecked((int)DateTime.UtcNow.Ticks ^ GetInstanceID());
        System.Random treeRandom = new System.Random(seed);
        System.Random materialRandom = new System.Random(unchecked(seed ^ MaterialSeedSalt));
        PrepareGeneratedMaterialPalettes(materialRandom);

        float scatterDiagonal = Mathf.Sqrt(scatterWidth * scatterWidth + scatterDepth * scatterDepth);
        bool spacingAllowsOnlyOneTree = treeCount > 1 && minimumSpacing > scatterDiagonal;
        int treesToAttempt = spacingAllowsOnlyOneTree ? 1 : treeCount;

        int createdThisPass = 0;
        for (int tree = 0; tree < treesToAttempt; tree++)
        {
            bool foundPosition = false;
            Vector3 position = scatterCentre;

            for (int attempt = 0; attempt < maxPlacementAttemptsPerTree; attempt++)
            {
                position = GetRandomPoint(treeRandom);
                if (IsFarEnoughFromExistingTrees(position))
                {
                    foundPosition = true;
                    break;
                }
            }

            if (!foundPosition)
            {
                continue;
            }

            CreateTree(nextTreeIndex, position, treeRandom, materialRandom);
            generatedTreePositions.Add(position);
            nextTreeIndex++;
            createdThisPass++;
        }

        if (createdThisPass < treeCount)
        {
            string reason = spacingAllowsOnlyOneTree
                ? $"Minimum spacing ({minimumSpacing:0.##}) exceeds the area's maximum point-to-point distance " +
                  $"({scatterDiagonal:0.##}), so no more than one tree can fit."
                : $"Area: {scatterWidth:0.##} x {scatterDepth:0.##}, minimum spacing: {minimumSpacing:0.##}, " +
                  $"attempts per tree: {maxPlacementAttemptsPerTree}. Increase the area, reduce spacing, " +
                  "or increase placement attempts.";

            Debug.LogWarning(
                $"{nameof(LowPolyRuntimeTreeGenerator)} placed {createdThisPass} of {treeCount} requested trees. {reason}",
                this);
        }
    }

    /// <summary>
    /// Removes all trees and runtime mesh/material resources made by this component.
    /// Assigned material assets are never destroyed.
    /// </summary>
    public void ClearTrees()
    {
        Transform generatedParentTransform = generatedParent != null
            ? generatedParent.transform
            : null;

        // Trees inside the generated parent are destroyed with it. Trees created
        // directly under this component still need to be destroyed individually.
        for (int i = generatedTreeRoots.Count - 1; i >= 0; i--)
        {
            GameObject treeRoot = generatedTreeRoots[i];
            if (treeRoot == null)
            {
                continue;
            }

            treeRoot.SetActive(false);
            bool destroyedWithParent = generatedParentTransform != null &&
                                       treeRoot.transform.IsChildOf(generatedParentTransform);
            if (!destroyedWithParent)
            {
                DestroyGeneratedObject(treeRoot);
            }
        }

        generatedTreeRoots.Clear();
        generatedTreePositions.Clear();
        treeInteractionRecords.Clear();

        if (generatedParent != null)
        {
            generatedParent.SetActive(false);
            DestroyGeneratedObject(generatedParent);
            generatedParent = null;
        }

        for (int i = 0; i < generatedMeshes.Count; i++)
        {
            DestroyGeneratedObject(generatedMeshes[i]);
        }

        for (int i = 0; i < generatedMaterials.Count; i++)
        {
            DestroyGeneratedObject(generatedMaterials[i]);
        }

        generatedMeshes.Clear();
        generatedMaterials.Clear();
        trunkMaterialPalette.Clear();
        leafMaterialPalette.Clear();
        trunkPaletteSignature = 0;
        leafPaletteSignature = 0;
        nextTreeIndex = 1;
    }

    /// <summary>
    /// Returns a horizontal steering direction that bends around nearby trunks.
    /// This is a lightweight positional query; it does not use Physics or colliders.
    /// </summary>
    public Vector3 GetTrunkAvoidanceDirection(
        Vector3 worldPosition,
        Vector3 desiredDirection,
        float clearance,
        float lookAheadDistance)
    {
        desiredDirection.y = 0f;
        if (desiredDirection.sqrMagnitude <= 0.000001f || treeInteractionRecords.Count == 0)
        {
            return desiredDirection.normalized;
        }

        clearance = Mathf.Max(0f, clearance);
        lookAheadDistance = Mathf.Max(0.01f, lookAheadDistance);
        Vector3 desired = desiredDirection.normalized;
        Vector3 steering = desired;

        for (int i = 0; i < treeInteractionRecords.Count; i++)
        {
            TreeInteractionRecord tree = treeInteractionRecords[i];
            if (!IsInteractionTreeValid(tree))
            {
                continue;
            }

            Vector3 centre = tree.Root.position;
            centre.y = worldPosition.y;
            float trunkRadius = GetWorldTrunkRadius(tree) + clearance;
            Vector3 toCentre = centre - worldPosition;
            float forwardDistance = Vector3.Dot(toCentre, desired);

            if (forwardDistance < -trunkRadius || forwardDistance > lookAheadDistance + trunkRadius)
            {
                continue;
            }

            Vector3 closestPoint = worldPosition + desired * Mathf.Clamp(
                forwardDistance,
                0f,
                lookAheadDistance);
            Vector3 fromCentre = closestPoint - centre;
            fromCentre.y = 0f;
            float distance = fromCentre.magnitude;
            float influenceRadius = trunkRadius + Mathf.Max(0.2f, lookAheadDistance * 0.4f);

            if (distance >= influenceRadius)
            {
                continue;
            }

            Vector3 normal = fromCentre;
            if (normal.sqrMagnitude <= 0.000001f)
            {
                normal = worldPosition - centre;
                normal.y = 0f;
            }

            if (normal.sqrMagnitude <= 0.000001f)
            {
                normal = (i & 1) == 0 ? Vector3.right : Vector3.left;
            }

            normal.Normalize();
            Vector3 tangent = Vector3.Cross(Vector3.up, normal).normalized;
            float tangentAlignment = Vector3.Dot(tangent, desired);
            float oppositeAlignment = Vector3.Dot(-tangent, desired);
            if (oppositeAlignment > tangentAlignment ||
                (Mathf.Abs(oppositeAlignment - tangentAlignment) < 0.01f && (i & 1) != 0))
            {
                tangent = -tangent;
            }

            float threat = 1f - Mathf.Clamp01(
                (distance - trunkRadius) /
                Mathf.Max(0.001f, influenceRadius - trunkRadius));
            steering += normal * (threat * 1.3f) + tangent * (threat * 0.9f);
        }

        steering.y = 0f;
        return steering.sqrMagnitude > 0.000001f ? steering.normalized : desired;
    }

    /// <summary>
    /// Projects a proposed ground position outside every trunk. It acts as a
    /// final safety constraint after steering and prevents visible penetration.
    /// </summary>
    public Vector3 ConstrainMovementAgainstTrunks(
        Vector3 currentWorldPosition,
        Vector3 proposedWorldPosition,
        float clearance)
    {
        clearance = Mathf.Max(0f, clearance);
        Vector3 constrained = proposedWorldPosition;

        for (int i = 0; i < treeInteractionRecords.Count; i++)
        {
            TreeInteractionRecord tree = treeInteractionRecords[i];
            if (!IsInteractionTreeValid(tree))
            {
                continue;
            }

            Vector3 basePosition = tree.Root.position;
            float trunkHeight = GetWorldTrunkHeight(tree);
            if (constrained.y < basePosition.y - clearance ||
                constrained.y > basePosition.y + trunkHeight + clearance)
            {
                continue;
            }

            float minimumRadius = GetWorldTrunkRadius(tree) + clearance;
            Vector3 offset = constrained - basePosition;
            offset.y = 0f;
            if (offset.sqrMagnitude >= minimumRadius * minimumRadius)
            {
                continue;
            }

            if (offset.sqrMagnitude <= 0.000001f)
            {
                offset = currentWorldPosition - basePosition;
                offset.y = 0f;
            }

            if (offset.sqrMagnitude <= 0.000001f)
            {
                offset = Vector3.right;
            }

            offset.Normalize();
            constrained.x = basePosition.x + offset.x * minimumRadius;
            constrained.z = basePosition.z + offset.z * minimumRadius;
        }

        return constrained;
    }

    /// <summary>
    /// Finds the nearest canopy top within a horizontal search distance.
    /// The excluded index supports a temporary same-tree landing cooldown.
    /// </summary>
    public bool TryGetCanopyLandingPoint(
        Vector3 worldPosition,
        float horizontalSearchDistance,
        int excludedTreeIndex,
        out Vector3 landingPoint,
        out int treeIndex)
    {
        landingPoint = worldPosition;
        treeIndex = -1;
        horizontalSearchDistance = Mathf.Max(0f, horizontalSearchDistance);
        float bestSurfaceDistance = float.PositiveInfinity;

        for (int i = 0; i < treeInteractionRecords.Count; i++)
        {
            if (i == excludedTreeIndex)
            {
                continue;
            }

            TreeInteractionRecord tree = treeInteractionRecords[i];
            if (!IsInteractionTreeValid(tree))
            {
                continue;
            }

            Vector3 lossyScale = Abs(tree.Root.lossyScale);
            for (int blobIndex = 0; blobIndex < tree.CanopyBlobs.Length; blobIndex++)
            {
                CanopyInteractionBlob blob = tree.CanopyBlobs[blobIndex];
                Vector3 centre = tree.Root.TransformPoint(blob.LocalCentre);
                float horizontalRadius = Mathf.Max(
                    blob.LocalRadii.x * lossyScale.x,
                    blob.LocalRadii.z * lossyScale.z);
                Vector2 horizontalDelta = new Vector2(
                    worldPosition.x - centre.x,
                    worldPosition.z - centre.z);
                float surfaceDistance = Mathf.Max(0f, horizontalDelta.magnitude - horizontalRadius);

                if (surfaceDistance > horizontalSearchDistance || surfaceDistance >= bestSurfaceDistance)
                {
                    continue;
                }

                float verticalRadius = blob.LocalRadii.y * lossyScale.y;
                landingPoint = centre + tree.Root.up * (verticalRadius + 0.05f);
                treeIndex = i;
                bestSurfaceDistance = surfaceDistance;
            }
        }

        return treeIndex >= 0;
    }

    /// <summary>Gets the ground centre of a generated tree by interaction index.</summary>
    public bool TryGetTreeCentre(int treeIndex, out Vector3 worldCentre)
    {
        if (treeIndex >= 0 && treeIndex < treeInteractionRecords.Count &&
            IsInteractionTreeValid(treeInteractionRecords[treeIndex]))
        {
            worldCentre = treeInteractionRecords[treeIndex].Root.position;
            return true;
        }

        worldCentre = default;
        return false;
    }

    /// <summary>
    /// Sweeps a sphere from one position to another against trunk cylinders and
    /// canopy spheres, returning the earliest hit for vector-based reflection.
    /// </summary>
    public bool TryResolveTreeCollision(
        Vector3 previousPosition,
        Vector3 proposedPosition,
        float movingRadius,
        out Vector3 resolvedPosition,
        out Vector3 hitNormal,
        out int treeIndex)
    {
        resolvedPosition = proposedPosition;
        hitNormal = Vector3.up;
        treeIndex = -1;
        movingRadius = Mathf.Max(0f, movingRadius);

        float earliestTime = float.PositiveInfinity;
        Vector3 earliestNormal = Vector3.up;
        int earliestTree = -1;

        for (int i = 0; i < treeInteractionRecords.Count; i++)
        {
            TreeInteractionRecord tree = treeInteractionRecords[i];
            if (!IsInteractionTreeValid(tree))
            {
                continue;
            }

            if (TrySweepTrunk(
                    tree,
                    previousPosition,
                    proposedPosition,
                    movingRadius,
                    out float trunkTime,
                    out Vector3 trunkNormal) &&
                trunkTime < earliestTime)
            {
                earliestTime = trunkTime;
                earliestNormal = trunkNormal;
                earliestTree = i;
            }

            Vector3 lossyScale = Abs(tree.Root.lossyScale);
            for (int blobIndex = 0; blobIndex < tree.CanopyBlobs.Length; blobIndex++)
            {
                CanopyInteractionBlob blob = tree.CanopyBlobs[blobIndex];
                Vector3 centre = tree.Root.TransformPoint(blob.LocalCentre);
                float canopyRadius = Mathf.Max(
                    blob.LocalRadii.x * lossyScale.x,
                    Mathf.Max(
                        blob.LocalRadii.y * lossyScale.y,
                        blob.LocalRadii.z * lossyScale.z));

                if (TrySweepSphere(
                        previousPosition,
                        proposedPosition,
                        centre,
                        canopyRadius + movingRadius,
                        out float canopyTime,
                        out Vector3 canopyNormal) &&
                    canopyTime < earliestTime)
                {
                    earliestTime = canopyTime;
                    earliestNormal = canopyNormal;
                    earliestTree = i;
                }
            }
        }

        if (earliestTree < 0)
        {
            return false;
        }

        hitNormal = earliestNormal.normalized;
        resolvedPosition = Vector3.Lerp(previousPosition, proposedPosition, earliestTime) + hitNormal * 0.01f;
        treeIndex = earliestTree;
        return true;
    }

    private static bool TrySweepTrunk(
        TreeInteractionRecord tree,
        Vector3 start,
        Vector3 end,
        float movingRadius,
        out float hitTime,
        out Vector3 hitNormal)
    {
        hitTime = float.PositiveInfinity;
        hitNormal = Vector3.up;

        Vector3 basePosition = tree.Root.position;
        Vector3 topPosition = tree.Root.TransformPoint(Vector3.up * tree.TrunkHeight);
        float bottomY = Mathf.Min(basePosition.y, topPosition.y);
        float topY = Mathf.Max(basePosition.y, topPosition.y);
        float expandedRadius = GetWorldTrunkRadius(tree) + movingRadius;

        Vector2 startXZ = new Vector2(start.x, start.z);
        Vector2 endXZ = new Vector2(end.x, end.z);
        Vector2 centreXZ = new Vector2(basePosition.x, basePosition.z);
        Vector2 movementXZ = endXZ - startXZ;
        Vector2 fromCentre = startXZ - centreXZ;
        float a = Vector2.Dot(movementXZ, movementXZ);
        float c = Vector2.Dot(fromCentre, fromCentre) - expandedRadius * expandedRadius;

        float sideTime = float.PositiveInfinity;
        Vector2 sideNormal = Vector2.right;

        if (c <= 0f)
        {
            sideNormal = fromCentre.sqrMagnitude > 0.000001f
                ? fromCentre.normalized
                : (movementXZ.sqrMagnitude > 0.000001f ? -movementXZ.normalized : Vector2.right);

            // Do not re-collide while an already-overlapping sphere moves out.
            if (Vector2.Dot(movementXZ, sideNormal) < 0f)
            {
                sideTime = 0f;
            }
        }
        else if (a > 0.000001f)
        {
            float b = Vector2.Dot(fromCentre, movementXZ);
            float discriminant = b * b - a * c;
            if (discriminant >= 0f)
            {
                float candidate = (-b - Mathf.Sqrt(discriminant)) / a;
                if (candidate >= 0f && candidate <= 1f)
                {
                    Vector2 hitXZ = startXZ + movementXZ * candidate;
                    sideNormal = (hitXZ - centreXZ).normalized;
                    sideTime = candidate;
                }
            }
        }

        if (sideTime <= 1f)
        {
            float hitY = Mathf.Lerp(start.y, end.y, sideTime);
            if (hitY >= bottomY - movingRadius && hitY <= topY + movingRadius)
            {
                hitTime = sideTime;
                hitNormal = new Vector3(sideNormal.x, 0f, sideNormal.y);
            }
        }

        float verticalMovement = end.y - start.y;
        if (Mathf.Abs(verticalMovement) > 0.000001f)
        {
            float topPlane = topY + movingRadius;
            if (verticalMovement < 0f && start.y >= topPlane && end.y <= topPlane)
            {
                float candidate = (topPlane - start.y) / verticalMovement;
                Vector3 point = Vector3.Lerp(start, end, candidate);
                Vector2 pointXZ = new Vector2(point.x, point.z);
                if ((pointXZ - centreXZ).sqrMagnitude <= expandedRadius * expandedRadius &&
                    candidate < hitTime)
                {
                    hitTime = candidate;
                    hitNormal = Vector3.up;
                }
            }

            float bottomPlane = bottomY - movingRadius;
            if (verticalMovement > 0f && start.y <= bottomPlane && end.y >= bottomPlane)
            {
                float candidate = (bottomPlane - start.y) / verticalMovement;
                Vector3 point = Vector3.Lerp(start, end, candidate);
                Vector2 pointXZ = new Vector2(point.x, point.z);
                if ((pointXZ - centreXZ).sqrMagnitude <= expandedRadius * expandedRadius &&
                    candidate < hitTime)
                {
                    hitTime = candidate;
                    hitNormal = Vector3.down;
                }
            }
        }

        return hitTime <= 1f;
    }

    private static bool TrySweepSphere(
        Vector3 start,
        Vector3 end,
        Vector3 sphereCentre,
        float combinedRadius,
        out float hitTime,
        out Vector3 hitNormal)
    {
        Vector3 movement = end - start;
        Vector3 fromCentre = start - sphereCentre;
        float c = Vector3.Dot(fromCentre, fromCentre) - combinedRadius * combinedRadius;

        if (c <= 0f)
        {
            hitNormal = fromCentre.sqrMagnitude > 0.000001f
                ? fromCentre.normalized
                : (movement.sqrMagnitude > 0.000001f ? -movement.normalized : Vector3.up);

            if (Vector3.Dot(movement, hitNormal) >= 0f)
            {
                hitTime = float.PositiveInfinity;
                return false;
            }

            hitTime = 0f;
            return true;
        }

        float a = Vector3.Dot(movement, movement);
        if (a <= 0.000001f)
        {
            hitTime = float.PositiveInfinity;
            hitNormal = Vector3.up;
            return false;
        }

        float b = Vector3.Dot(fromCentre, movement);
        float discriminant = b * b - a * c;
        if (discriminant < 0f)
        {
            hitTime = float.PositiveInfinity;
            hitNormal = Vector3.up;
            return false;
        }

        hitTime = (-b - Mathf.Sqrt(discriminant)) / a;
        if (hitTime < 0f || hitTime > 1f)
        {
            hitNormal = Vector3.up;
            return false;
        }

        Vector3 hitPoint = Vector3.Lerp(start, end, hitTime);
        hitNormal = (hitPoint - sphereCentre).normalized;
        return true;
    }

    private static bool IsInteractionTreeValid(TreeInteractionRecord tree)
    {
        return tree != null && tree.Root != null && tree.Root.gameObject.activeInHierarchy;
    }

    private static float GetWorldTrunkRadius(TreeInteractionRecord tree)
    {
        Vector3 scale = Abs(tree.Root.lossyScale);
        return tree.TrunkRadius * Mathf.Max(scale.x, scale.z);
    }

    private static float GetWorldTrunkHeight(TreeInteractionRecord tree)
    {
        return tree.Root.TransformVector(Vector3.up * tree.TrunkHeight).magnitude;
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private void CreateTree(
        int treeIndex,
        Vector3 localPosition,
        System.Random random,
        System.Random materialRandom)
    {
        string treeName = $"GeneratedTree_{treeIndex:000}";
        GameObject treeRoot = new GameObject(treeName);
        Transform treeTransform = treeRoot.transform;
        treeTransform.SetParent(GetGeneratedTreeParent(), false);
        treeTransform.localPosition = localPosition;
        treeTransform.localRotation = randomRotation
            ? Quaternion.Euler(0f, Range(random, 0f, 360f), 0f)
            : Quaternion.identity;

        float treeScale = randomScale
            ? Range(random, minimumTreeScale, maximumTreeScale)
            : 1f;
        treeTransform.localScale = Vector3.one * treeScale;

        float trunkHeight = Range(random, trunkHeightRange.x, trunkHeightRange.y);
        float trunkRadius = Range(random, trunkRadiusRange.x, trunkRadiusRange.y);
        int trunkSides = RangeInclusive(random, trunkSideCountRange.x, trunkSideCountRange.y);

        int materialVariantIndex = autoGenerateMaterials
            ? materialRandom.Next(generatedMaterialVariants)
            : 0;
        Material trunkMaterial = ResolveTreeMaterial(assignedTrunkMaterial, true, materialVariantIndex);
        Material leafMaterial = ResolveTreeMaterial(assignedLeafMaterial, false, materialVariantIndex);

        int branchCount = RangeInclusive(random, branchCountRange.x, branchCountRange.y);
        int branchSides = Mathf.Clamp(trunkSides - 1, 4, 6);
        List<Mesh> woodSourceMeshes = new List<Mesh>(branchCount + 1);
        List<CombineInstance> woodParts = new List<CombineInstance>(branchCount + 1);

        Mesh trunkMesh = CreateTrunkMesh(trunkHeight, trunkRadius, trunkSides);
        woodSourceMeshes.Add(trunkMesh);
        woodParts.Add(new CombineInstance
        {
            mesh = trunkMesh,
            transform = Matrix4x4.identity
        });

        for (int branchIndex = 0; branchIndex < branchCount; branchIndex++)
        {
            float branchLength = Range(random, branchLengthRange.x, branchLengthRange.y);
            float branchRadius = Mathf.Min(
                Range(random, branchRadiusRange.x, branchRadiusRange.y),
                trunkRadius * 0.55f);
            float branchBaseHeight = Range(random, trunkHeight * 0.46f, trunkHeight * 0.78f);

            // Roughly distribute branches around the trunk, then add enough jitter
            // that repeated trees do not look mechanically radial.
            float evenAngle = branchCount > 0 ? (360f * branchIndex / branchCount) : 0f;
            float angle = evenAngle + Range(random, -35f, 35f);
            float angleRadians = angle * Mathf.Deg2Rad;
            Vector3 outward = new Vector3(Mathf.Cos(angleRadians), 0f, Mathf.Sin(angleRadians));
            Vector3 branchDirection = (outward + Vector3.up * Range(random, 0.32f, 0.7f)).normalized;

            Mesh branchMesh = CreateBranchMesh(branchLength, branchRadius, branchSides);
            Quaternion branchRotation = Quaternion.FromToRotation(Vector3.up, branchDirection);
            woodSourceMeshes.Add(branchMesh);
            woodParts.Add(new CombineInstance
            {
                mesh = branchMesh,
                transform = Matrix4x4.TRS(
                    new Vector3(0f, branchBaseHeight, 0f),
                    branchRotation,
                    Vector3.one)
            });
        }

        Mesh combinedWoodMesh = CombineAndReleaseSourceMeshes(
            woodParts,
            woodSourceMeshes,
            "ProceduralTree_TrunkAndBranchesMesh");
        CreateMeshPart(
            "TrunkAndBranches",
            treeTransform,
            combinedWoodMesh,
            trunkMaterial,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one);

        float baseCanopySize = Range(random, canopySizeRange.x, canopySizeRange.y);
        float baseCanopyHeight = Range(random, canopyHeightRange.x, canopyHeightRange.y);
        List<Mesh> leafSourceMeshes = new List<Mesh>(canopyBlobsPerTree);
        List<CombineInstance> leafParts = new List<CombineInstance>(canopyBlobsPerTree);
        CanopyInteractionBlob[] interactionBlobs = new CanopyInteractionBlob[canopyBlobsPerTree];

        for (int blobIndex = 0; blobIndex < canopyBlobsPerTree; blobIndex++)
        {
            Vector2 spreadOffset = blobIndex == 0
                ? Vector2.zero
                : GetRandomPointInDisc(random, canopyBlobSpread);

            float blobWidth = baseCanopySize * Range(random, 0.78f, 1.08f);
            float blobDepth = blobWidth * Range(random, 0.88f, 1.12f);
            float blobHeight = baseCanopyHeight * Range(random, 0.82f, 1.12f);
            float heightOffset = Range(random, -baseCanopyHeight * 0.14f, baseCanopyHeight * 0.14f);

            Vector3 blobPosition = new Vector3(
                spreadOffset.x,
                trunkHeight + baseCanopyHeight * 0.1f + heightOffset,
                spreadOffset.y);
            Quaternion blobRotation = Quaternion.Euler(
                Range(random, -12f, 12f),
                Range(random, 0f, 360f),
                Range(random, -12f, 12f));
            Vector3 blobScale = new Vector3(blobWidth, blobHeight, blobDepth);

            interactionBlobs[blobIndex] = new CanopyInteractionBlob
            {
                LocalCentre = blobPosition,
                // The source blob extends slightly beyond half a unit because
                // of its random faceting, so 0.56 safely encloses its silhouette.
                LocalRadii = blobScale * 0.56f
            };

            Mesh leafMesh = CreateLeafBlobMesh(random);
            leafSourceMeshes.Add(leafMesh);
            leafParts.Add(new CombineInstance
            {
                mesh = leafMesh,
                transform = Matrix4x4.TRS(blobPosition, blobRotation, blobScale)
            });
        }

        Mesh combinedLeafMesh = CombineAndReleaseSourceMeshes(
            leafParts,
            leafSourceMeshes,
            "ProceduralTree_CanopyMesh");
        CreateMeshPart(
            "Canopy",
            treeTransform,
            combinedLeafMesh,
            leafMaterial,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one);

        generatedTreeRoots.Add(treeRoot);
        treeInteractionRecords.Add(new TreeInteractionRecord
        {
            Root = treeTransform,
            TrunkHeight = trunkHeight,
            TrunkRadius = trunkRadius,
            CanopyBlobs = interactionBlobs
        });
    }

    /// <summary>Creates a vertically aligned, tapered, flat-shaded trunk mesh.</summary>
    private Mesh CreateTrunkMesh(float height, float radius, int sides)
    {
        return CreateTaperedCylinderMesh(
            height,
            radius,
            radius * 0.62f,
            sides,
            "ProceduralTree_TrunkMesh");
    }

    /// <summary>Creates a tapered cylinder pointing along its local positive Y axis.</summary>
    private Mesh CreateBranchMesh(float length, float radius, int sides)
    {
        return CreateTaperedCylinderMesh(
            length,
            radius,
            radius * 0.28f,
            sides,
            "ProceduralTree_BranchMesh");
    }

    /// <summary>
    /// Creates a small faceted organic blob. Every triangle owns its vertices,
    /// guaranteeing hard edges and flat shading after normal recalculation.
    /// </summary>
    private Mesh CreateLeafBlobMesh(System.Random random)
    {
        int segments = RangeInclusive(random, 5, 7);
        Vector3[] upperRing = new Vector3[segments];
        Vector3[] lowerRing = new Vector3[segments];
        float angleStep = Mathf.PI * 2f / segments;

        for (int i = 0; i < segments; i++)
        {
            float upperAngle = i * angleStep;
            float lowerAngle = upperAngle + angleStep * 0.35f;
            float upperRadius = Range(random, 0.42f, 0.54f);
            float lowerRadius = Range(random, 0.39f, 0.51f);

            upperRing[i] = new Vector3(
                Mathf.Cos(upperAngle) * upperRadius,
                Range(random, 0.11f, 0.22f),
                Mathf.Sin(upperAngle) * upperRadius);
            lowerRing[i] = new Vector3(
                Mathf.Cos(lowerAngle) * lowerRadius,
                Range(random, -0.27f, -0.16f),
                Mathf.Sin(lowerAngle) * lowerRadius);
        }

        Vector3 top = new Vector3(
            Range(random, -0.06f, 0.06f),
            Range(random, 0.47f, 0.56f),
            Range(random, -0.06f, 0.06f));
        Vector3 bottom = new Vector3(
            Range(random, -0.05f, 0.05f),
            Range(random, -0.55f, -0.46f),
            Range(random, -0.05f, 0.05f));

        int triangleCount = segments * 4;
        Vector3[] vertices = new Vector3[triangleCount * 3];
        int[] triangles = new int[vertices.Length];
        int vertexIndex = 0;

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;

            // Winding is outward for the top fan, ring sides, and bottom fan.
            AddFlatTriangle(vertices, triangles, ref vertexIndex, top, upperRing[next], upperRing[i]);
            AddFlatTriangle(vertices, triangles, ref vertexIndex, upperRing[i], upperRing[next], lowerRing[next]);
            AddFlatTriangle(vertices, triangles, ref vertexIndex, upperRing[i], lowerRing[next], lowerRing[i]);
            AddFlatTriangle(vertices, triangles, ref vertexIndex, bottom, lowerRing[i], lowerRing[next]);
        }

        Mesh mesh = new Mesh
        {
            name = "ProceduralTree_LeafBlobMesh",
            vertices = vertices,
            triangles = triangles
        };
        return mesh;
    }

    /// <summary>Creates a simple material without assigning any textures.</summary>
    private Material CreateMaterial(Color colour, string materialName)
    {
        Shader shader = FindCompatibleUnlitShader();
        if (shader == null)
        {
            Debug.LogError(
                $"{nameof(LowPolyRuntimeTreeGenerator)} could not find a compatible unlit colour shader. " +
                "Assign trunk and leaf materials or include an unlit shader in the build.",
                this);
            return null;
        }

        colour.a = 1f;
        Material material = new Material(shader)
        {
            name = materialName,
            enableInstancing = true
        };

        // URP Unlit uses _BaseColor; Built-in Unlit/Color uses _Color.
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", colour);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", colour);
        }

        // Keep URP Unlit opaque even if its imported defaults were changed.
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 0f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_AlphaClip"))
        {
            material.SetFloat("_AlphaClip", 0f);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 1f);
        }

        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = -1;

        generatedMaterials.Add(material);
        return material;
    }

    private Vector3 GetRandomPoint(System.Random random)
    {
        float halfWidth = scatterWidth * 0.5f;
        float halfDepth = scatterDepth * 0.5f;

        return new Vector3(
            scatterCentre.x + Range(random, -halfWidth, halfWidth),
            scatterCentre.y,
            scatterCentre.z + Range(random, -halfDepth, halfDepth));
    }

    private bool IsFarEnoughFromExistingTrees(Vector3 candidate)
    {
        if (minimumSpacing <= 0f)
        {
            return true;
        }

        float minimumSpacingSquared = minimumSpacing * minimumSpacing;
        for (int i = 0; i < generatedTreePositions.Count; i++)
        {
            Vector3 delta = candidate - generatedTreePositions[i];
            delta.y = 0f;
            if (delta.sqrMagnitude < minimumSpacingSquared)
            {
                return false;
            }
        }

        return true;
    }

    private Transform GetGeneratedTreeParent()
    {
        if (!parentGeneratedTrees)
        {
            return transform;
        }

        if (generatedParent == null)
        {
            generatedParent = new GameObject(generatedParentName);
            generatedParent.transform.SetParent(transform, false);
        }

        return generatedParent.transform;
    }

    private void PrepareGeneratedMaterialPalettes(System.Random random)
    {
        if (!useAssignedMaterials || assignedTrunkMaterial == null)
        {
            BuildOrReuseMaterialPalette(trunkMaterialPalette, true, random);
        }

        if (!useAssignedMaterials || assignedLeafMaterial == null)
        {
            BuildOrReuseMaterialPalette(leafMaterialPalette, false, random);
        }
    }

    private void BuildOrReuseMaterialPalette(
        List<Material> palette,
        bool isTrunk,
        System.Random random)
    {
        Color colourA = isTrunk ? trunkColourA : leafColourA;
        Color colourB = isTrunk ? trunkColourB : leafColourB;
        int variantCount = autoGenerateMaterials ? generatedMaterialVariants : 1;
        int signature = GetPaletteSignature(colourA, colourB, variantCount, autoGenerateMaterials);
        int currentSignature = isTrunk ? trunkPaletteSignature : leafPaletteSignature;

        if (palette.Count == variantCount && currentSignature == signature)
        {
            return;
        }

        // Old materials remain tracked until ClearTrees because existing trees
        // may still reference them when additive generation is enabled.
        palette.Clear();
        for (int variant = 0; variant < variantCount; variant++)
        {
            float colourPosition;
            if (!autoGenerateMaterials)
            {
                colourPosition = 0.5f;
            }
            else
            {
                // Stratified sampling covers the colour range without producing
                // a cluster of nearly identical palette entries.
                colourPosition = (variant + NextFloat(random)) / variantCount;
            }

            string partName = isTrunk ? "Trunk" : "Leaf";
            Material material = CreateMaterial(
                Color.Lerp(colourA, colourB, colourPosition),
                $"GeneratedTree_{partName}Palette_{variant + 1:00}");
            palette.Add(material);
        }

        if (isTrunk)
        {
            trunkPaletteSignature = signature;
        }
        else
        {
            leafPaletteSignature = signature;
        }
    }

    private Material ResolveTreeMaterial(
        Material assignedMaterial,
        bool isTrunk,
        int materialVariantIndex)
    {
        if (useAssignedMaterials && assignedMaterial != null)
        {
            return assignedMaterial;
        }

        List<Material> palette = isTrunk ? trunkMaterialPalette : leafMaterialPalette;
        if (palette.Count == 0)
        {
            return null;
        }

        return palette[materialVariantIndex % palette.Count];
    }

    private static int GetPaletteSignature(
        Color colourA,
        Color colourB,
        int variantCount,
        bool useVariation)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + colourA.GetHashCode();
            hash = hash * 31 + colourB.GetHashCode();
            hash = hash * 31 + variantCount;
            hash = hash * 31 + (useVariation ? 1 : 0);
            return hash;
        }
    }

    private Shader FindCompatibleUnlitShader()
    {
        if (cachedUnlitShader != null)
        {
            return cachedUnlitShader;
        }

        RenderPipelineAsset currentPipeline = GraphicsSettings.currentRenderPipeline;
        bool isUniversalPipeline = currentPipeline != null &&
            currentPipeline.GetType().Name.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0;

        if (isUniversalPipeline)
        {
            cachedUnlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        }
        else if (currentPipeline == null)
        {
            cachedUnlitShader = Shader.Find("Unlit/Color");
        }

        // Sprite shaders are also unlit and use a white default texture, making
        // them safe colour-only fallbacks if the preferred shader was stripped.
        if (cachedUnlitShader == null && isUniversalPipeline)
        {
            cachedUnlitShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        }

        if (cachedUnlitShader == null && currentPipeline == null)
        {
            cachedUnlitShader = Shader.Find("Sprites/Default");
        }

        return cachedUnlitShader;
    }

    private static Mesh CreateTaperedCylinderMesh(
        float height,
        float bottomRadius,
        float topRadius,
        int sides,
        string meshName)
    {
        sides = Mathf.Max(3, sides);

        // Side faces own their four vertices. Each planar cap shares a separate
        // centre/ring, reducing vertex count without smoothing into the sides.
        int sideVertexCount = sides * 4;
        int bottomCentreIndex = sideVertexCount;
        int bottomRingStart = bottomCentreIndex + 1;
        int topCentreIndex = bottomRingStart + sides;
        int topRingStart = topCentreIndex + 1;
        Vector3[] vertices = new Vector3[sides * 6 + 2];
        int[] triangles = new int[sides * 12];
        int triangleIndex = 0;
        float angleStep = Mathf.PI * 2f / sides;

        vertices[bottomCentreIndex] = Vector3.zero;
        vertices[topCentreIndex] = new Vector3(0f, height, 0f);

        for (int side = 0; side < sides; side++)
        {
            float angle0 = side * angleStep;
            float angle1 = (side + 1) * angleStep;

            Vector3 bottom0 = new Vector3(Mathf.Cos(angle0) * bottomRadius, 0f, Mathf.Sin(angle0) * bottomRadius);
            Vector3 bottom1 = new Vector3(Mathf.Cos(angle1) * bottomRadius, 0f, Mathf.Sin(angle1) * bottomRadius);
            Vector3 top0 = new Vector3(Mathf.Cos(angle0) * topRadius, height, Mathf.Sin(angle0) * topRadius);
            Vector3 top1 = new Vector3(Mathf.Cos(angle1) * topRadius, height, Mathf.Sin(angle1) * topRadius);

            int sideStart = side * 4;
            vertices[sideStart] = bottom0;
            vertices[sideStart + 1] = top0;
            vertices[sideStart + 2] = top1;
            vertices[sideStart + 3] = bottom1;

            triangles[triangleIndex++] = sideStart;
            triangles[triangleIndex++] = sideStart + 1;
            triangles[triangleIndex++] = sideStart + 2;
            triangles[triangleIndex++] = sideStart;
            triangles[triangleIndex++] = sideStart + 2;
            triangles[triangleIndex++] = sideStart + 3;

            vertices[bottomRingStart + side] = bottom0;
            vertices[topRingStart + side] = top0;

            int nextSide = (side + 1) % sides;

            // Increasing ring angles face the bottom cap down.
            triangles[triangleIndex++] = bottomCentreIndex;
            triangles[triangleIndex++] = bottomRingStart + side;
            triangles[triangleIndex++] = bottomRingStart + nextSide;

            // Reversed ring order faces the top cap up.
            triangles[triangleIndex++] = topCentreIndex;
            triangles[triangleIndex++] = topRingStart + nextSide;
            triangles[triangleIndex++] = topRingStart + side;
        }

        Mesh mesh = new Mesh
        {
            name = meshName,
            vertices = vertices,
            triangles = triangles
        };
        return mesh;
    }

    /// <summary>
    /// Bakes several procedural pieces into one renderable mesh, then releases
    /// the temporary source meshes. This keeps every tree to two draw renderers:
    /// one for wood and one for leaves.
    /// </summary>
    private Mesh CombineAndReleaseSourceMeshes(
        List<CombineInstance> parts,
        List<Mesh> sourceMeshes,
        string meshName)
    {
        Mesh combinedMesh = new Mesh
        {
            name = meshName
        };
        combinedMesh.CombineMeshes(parts.ToArray(), true, true, false);
        // Source faces do not share vertices across hard edges, so recalculating
        // here preserves flat shading and correctly handles baked non-uniform scales.
        combinedMesh.RecalculateNormals();
        combinedMesh.RecalculateBounds();
        combinedMesh.UploadMeshData(true);
        generatedMeshes.Add(combinedMesh);

        for (int i = 0; i < sourceMeshes.Count; i++)
        {
            DestroyGeneratedObject(sourceMeshes[i]);
        }

        return combinedMesh;
    }

    private void CreateMeshPart(
        string objectName,
        Transform parent,
        Mesh mesh,
        Material material,
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale)
    {
        GameObject part = new GameObject(objectName);
        Transform partTransform = part.transform;
        partTransform.SetParent(parent, false);
        partTransform.localPosition = localPosition;
        partTransform.localRotation = localRotation;
        partTransform.localScale = localScale;

        MeshFilter meshFilter = part.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        MeshRenderer meshRenderer = part.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = castShadows
            ? ShadowCastingMode.On
            : ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private static void AddFlatTriangle(
        Vector3[] vertices,
        int[] triangles,
        ref int vertexIndex,
        Vector3 a,
        Vector3 b,
        Vector3 c)
    {
        vertices[vertexIndex] = a;
        triangles[vertexIndex] = vertexIndex;
        vertexIndex++;

        vertices[vertexIndex] = b;
        triangles[vertexIndex] = vertexIndex;
        vertexIndex++;

        vertices[vertexIndex] = c;
        triangles[vertexIndex] = vertexIndex;
        vertexIndex++;
    }

    private static Vector2 GetRandomPointInDisc(System.Random random, float radius)
    {
        float angle = Range(random, 0f, Mathf.PI * 2f);
        float distance = Mathf.Sqrt(NextFloat(random)) * radius;
        return new Vector2(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance);
    }

    private static float Range(System.Random random, float minimum, float maximum)
    {
        return Mathf.Lerp(minimum, maximum, NextFloat(random));
    }

    private static int RangeInclusive(System.Random random, int minimum, int maximum)
    {
        return random.Next(minimum, maximum + 1);
    }

    private static float NextFloat(System.Random random)
    {
        return (float)random.NextDouble();
    }

    private static void DestroyGeneratedObject(UnityEngine.Object generatedObject)
    {
        if (generatedObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(generatedObject);
        }
        else
        {
            DestroyImmediate(generatedObject);
        }
    }

    private void OnValidate()
    {
        ValidateSettings();
    }

    private void ValidateSettings()
    {
        treeCount = Mathf.Clamp(treeCount, 0, MaxTreeCount);
        scatterWidth = Mathf.Max(0f, scatterWidth);
        scatterDepth = Mathf.Max(0f, scatterDepth);
        minimumSpacing = Mathf.Max(0f, minimumSpacing);
        maxPlacementAttemptsPerTree = Mathf.Clamp(
            maxPlacementAttemptsPerTree,
            1,
            MaxPlacementAttempts);

        minimumTreeScale = Mathf.Max(0.01f, minimumTreeScale);
        maximumTreeScale = Mathf.Max(minimumTreeScale, maximumTreeScale);

        ClampRange(ref trunkHeightRange, 0.1f);
        ClampRange(ref trunkRadiusRange, 0.01f);
        trunkSideCountRange.x = Mathf.Clamp(trunkSideCountRange.x, 3, MaxTrunkSides);
        trunkSideCountRange.y = Mathf.Clamp(
            trunkSideCountRange.y,
            trunkSideCountRange.x,
            MaxTrunkSides);

        ClampRange(ref canopySizeRange, 0.1f);
        ClampRange(ref canopyHeightRange, 0.1f);
        canopyBlobsPerTree = Mathf.Clamp(canopyBlobsPerTree, 1, MaxCanopyBlobCount);
        canopyBlobSpread = Mathf.Max(0f, canopyBlobSpread);

        branchCountRange.x = Mathf.Clamp(branchCountRange.x, 0, MaxBranchCount);
        branchCountRange.y = Mathf.Clamp(
            branchCountRange.y,
            branchCountRange.x,
            MaxBranchCount);
        ClampRange(ref branchLengthRange, 0.05f);
        ClampRange(ref branchRadiusRange, 0.005f);
        generatedMaterialVariants = Mathf.Clamp(
            generatedMaterialVariants,
            1,
            MaxMaterialVariants);

        if (string.IsNullOrWhiteSpace(generatedParentName))
        {
            generatedParentName = "Generated Low Poly Trees";
        }
    }

    private static void ClampRange(ref Vector2 range, float absoluteMinimum)
    {
        range.x = Mathf.Max(absoluteMinimum, range.x);
        range.y = Mathf.Max(range.x, range.y);
    }

    private void OnDrawGizmosSelected()
    {
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Color oldColour = Gizmos.color;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.25f, 0.85f, 0.35f, 0.85f);
        Gizmos.DrawWireCube(
            scatterCentre,
            new Vector3(Mathf.Max(0f, scatterWidth), 0.05f, Mathf.Max(0f, scatterDepth)));

        Gizmos.matrix = oldMatrix;
        Gizmos.color = oldColour;
    }
}
