using UnityEngine;

namespace Goodgulf.Builder
{
    /// <summary>
    /// Shows the outline of a BuildingArea on a terrain.
    /// Useful for debugging physics interactions and placement of building blocks.
    /// </summary>


    // Ensures that a LineRenderer component is always present on this GameObject
    [RequireComponent(typeof(LineRenderer))]
    public class BuildingAreaOutline : MonoBehaviour
    {
        // Reference to the terrain used to sample height values
        public Terrain terrain;

        // Size of the square building area (world units)
        public float areaSize = 100f;

        // Number of sampled points per edge of the square
        // Higher values = smoother outline but more expensive
        public int samplesPerEdge = 25;

        // Small vertical offset so the line doesn't clip into the terrain
        public float yOffset = 0.1f;

        // Line color when placement is valid
        public Color validColor = Color.green;

        // Line color when placement is invalid
        public Color invalidColor = Color.red;

        // Cached reference to the LineRenderer
        LineRenderer line;

        void Awake()
        {
            // Get the LineRenderer attached to this GameObject
            line = GetComponent<LineRenderer>();

            // Close the line into a loop (square outline)
            line.loop = true;

            // Use world-space coordinates so the outline follows terrain properly
            line.useWorldSpace = true;

            // Total number of points needed for all four edges
            line.positionCount = samplesPerEdge * 4;
        }

        void Update()
        {
            // Update the outline every frame to follow position and terrain changes
            UpdateOutline();
        }

        void UpdateOutline()
        {
            // Half-size is used to build the square around the center
            float half = areaSize * 0.5f;

            // Center point of the building area
            Vector3 center = transform.position;

            // Index used to assign positions in the LineRenderer
            int index = 0;

            // ---- Bottom edge (left to right) ----
            for (int i = 0; i < samplesPerEdge; i++)
            {
                // Normalized interpolation value (0 → 1)
                float t = i / (float)(samplesPerEdge - 1);

                // Calculate horizontal position along the bottom edge
                Vector3 p = center + new Vector3(
                    Mathf.Lerp(-half, half, t),
                    0,
                    -half
                );

                // Sample terrain height and apply vertical offset
                p.y = terrain.SampleHeight(p) + yOffset;

                // Assign position to the LineRenderer
                line.SetPosition(index++, p);
            }

            // ---- Right edge (bottom to top) ----
            for (int i = 0; i < samplesPerEdge; i++)
            {
                float t = i / (float)(samplesPerEdge - 1);

                Vector3 p = center + new Vector3(
                    half,
                    0,
                    Mathf.Lerp(-half, half, t)
                );

                p.y = terrain.SampleHeight(p) + yOffset;
                line.SetPosition(index++, p);
            }

            // ---- Top edge (right to left) ----
            for (int i = 0; i < samplesPerEdge; i++)
            {
                float t = i / (float)(samplesPerEdge - 1);

                Vector3 p = center + new Vector3(
                    Mathf.Lerp(half, -half, t),
                    0,
                    half
                );

                p.y = terrain.SampleHeight(p) + yOffset;
                line.SetPosition(index++, p);
            }

            // ---- Left edge (top to bottom) ----
            for (int i = 0; i < samplesPerEdge; i++)
            {
                float t = i / (float)(samplesPerEdge - 1);

                Vector3 p = center + new Vector3(
                    -half,
                    0,
                    Mathf.Lerp(half, -half, t)
                );

                p.y = terrain.SampleHeight(p) + yOffset;
                line.SetPosition(index++, p);
            }
        }

        // Sets the outline color based on whether the area is valid for building
        public void SetValid(bool valid)
        {
            // Apply the same color to both ends of the line
            line.startColor = valid ? validColor : invalidColor;
            line.endColor   = valid ? validColor : invalidColor;
        }
    }
}
