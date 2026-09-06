using UnityEngine;

namespace GrokDoorwayLighting
{
    /// <summary>
    /// Doorway treated as an opening, not a glowing slab. Spill falls into the
    /// dark room. The swinging door itself is excluded separately.
    /// </summary>
    public static class GrokDoorwayMath
    {
        public struct DoorwayFrame
        {
            public Vector3 position;
            public Vector3 inward;
            public Vector3 right;
            public Vector3 up;
            public float halfWidth;
            public float halfHeight;
            public float blendDepth;
            public float lateralFade;
            public float verticalPadding;
            public float jambAllowance;
        }

        public static float Smooth01(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - 2f * t);
        }

        public static DoorwayFrame CreateFrame(
            Transform doorway,
            float receiverInteriorSign,
            float blendDepth,
            float lateralFade,
            float verticalPadding,
            float jambAllowance)
        {
            Vector2 socketSize = new Vector2(1f, 2.5f);
            var component = doorway != null ? doorway.GetComponent<DunGen.Doorway>() : null;
            if (component != null && component.Socket != null)
                socketSize = component.Socket.Size;

            Vector3 inward = doorway.forward * receiverInteriorSign;
            inward.Normalize();

            return new DoorwayFrame
            {
                position = doorway.position,
                inward = inward,
                right = doorway.right.normalized,
                up = doorway.up.normalized,
                halfWidth = Mathf.Max(0.05f, socketSize.x * 0.5f),
                halfHeight = Mathf.Max(0.05f, socketSize.y * 0.5f),
                blendDepth = Mathf.Max(0.05f, blendDepth),
                lateralFade = Mathf.Max(0f, lateralFade),
                verticalPadding = Mathf.Max(0f, verticalPadding),
                jambAllowance = Mathf.Max(0f, jambAllowance)
            };
        }

        public static float ComputeWeight(Vector3 positionWs, Vector3 normalWs, in DoorwayFrame door)
        {
            Vector3 rel = positionWs - door.position;
            float depth = Vector3.Dot(rel, door.inward);
            if (depth < -door.jambAllowance)
                return 0f;

            float lateral = Mathf.Abs(Vector3.Dot(rel, door.right));
            float height = Vector3.Dot(rel, door.up);
            float outsideLateral = Mathf.Max(0f, lateral - door.halfWidth);
            float below = Mathf.Max(0f, -height);
            float above = Mathf.Max(0f, height - door.halfHeight * 2f);
            float outsideVertical = Mathf.Max(below, above);

            Vector3 normal = normalWs.sqrMagnitude > 0.000001f ? normalWs.normalized : Vector3.up;
            float upAlign = Mathf.Abs(Vector3.Dot(normal, door.up));
            bool isFloor = upAlign >= 0.55f;
            float transverse = 1f - Mathf.Abs(Vector3.Dot(normal, door.inward));
            bool isJamb = !isFloor && transverse > 0.45f;

            // The door leaf sits in the opening. Do not treat that slab as a light.
            bool onDoorPlane = depth > -0.04f &&
                               depth < 0.08f &&
                               outsideLateral <= 0.03f &&
                               outsideVertical <= 0.03f;
            if (onDoorPlane && !isJamb)
                return 0f;

            Vector3 openingCenter = door.position + door.inward * 0.05f + door.up * door.halfHeight;
            Vector3 toOpening = openingCenter - positionWs;
            float dist = toOpening.magnitude;
            Vector3 toLight = dist > 0.0001f ? toOpening / dist : -door.inward;
            float nDotL = Mathf.Clamp01(Vector3.Dot(normal, toLight));
            float distT = Smooth01(1f - Mathf.Max(depth, 0f) / door.blendDepth);
            float invSq = 1f / (1f + dist * dist * 0.12f);

            if (isJamb && depth < 0.35f)
            {
                float jambAlong = Smooth01(1f - Mathf.Max(outsideLateral, outsideVertical) / 0.22f);
                return 0.5f * jambAlong * Mathf.Max(nDotL, 0.35f) * distT;
            }

            if (depth < 0.06f)
                return 0f;

            float lateralFade = Mathf.Max(door.lateralFade, 0.20f);
            float lateralT = Smooth01(1f - outsideLateral / lateralFade);
            float verticalT = Smooth01(1f - outsideVertical / Mathf.Max(0.05f, door.verticalPadding + 0.35f));
            float facing = isFloor ? Mathf.Max(nDotL, 0.35f) : nDotL;
            return distT * lateralT * verticalT * facing * invSq;
        }

        public static bool IsDoorOccluder(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (current.GetComponent<DunGen.Door>() != null)
                    return true;

                string name = current.name;
                if (name.IndexOf("Blocker_", System.StringComparison.Ordinal) >= 0)
                    return true;
                if (name.IndexOf("DoorPlacement", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (name.IndexOf("Door_Placement", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (name.IndexOf("DungeonDoorProbeSplitRoot", System.StringComparison.Ordinal) >= 0)
                    return true;
            }

            return false;
        }

        public static Vector2 ComputePortalUv(Vector3 positionWs, in DoorwayFrame door)
        {
            Vector3 rel = positionWs - door.position;
            float u = Vector3.Dot(rel, door.right) / Mathf.Max(0.05f, door.halfWidth * 2f) + 0.5f;
            float v = Vector3.Dot(rel, door.up) / Mathf.Max(0.05f, door.halfHeight * 2f);
            return new Vector2(u, v);
        }

        public static bool IsTransferActive(
            bool visualizationEnabled,
            DungeonTileLightmapSwitcher.PowerLevel sourcePower,
            DungeonTileLightmapSwitcher.PowerLevel receiverPower)
        {
            return visualizationEnabled &&
                   sourcePower == DungeonTileLightmapSwitcher.PowerLevel.P100 &&
                   receiverPower == DungeonTileLightmapSwitcher.PowerLevel.P0;
        }
    }
}
