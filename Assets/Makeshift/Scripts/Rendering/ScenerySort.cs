using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Makeshift.Rendering
{
    /// <summary>
    /// Picks, from everything <see cref="SceneryInstancer"/> could draw, what is in view and at which detail level,
    /// and lines the matrices up by batch. It runs as a Burst job, so the main thread only asks and collects.
    ///
    /// Describe the scenery once with <see cref="Begin"/>, <see cref="Item"/>, <see cref="Level"/>, <see cref="Part"/>,
    /// <see cref="Uses"/> and <see cref="End"/>; then <see cref="Ask"/> whenever the view moves. There are two sets of
    /// results: one is drawn from while the next is being filled.
    /// </summary>
    public sealed class ScenerySort : IDisposable
    {
        private struct ItemData
        {
            public float3 centre;
            public float radius;
            public int casts, firstLevel, levels;
        }

        private struct LevelData
        {
            public float reach;     // used nearer than this
            public int firstPart, parts;
        }

        private struct PartData
        {
            public Matrix4x4 matrix;
            public float3 min, max;
            public int firstUse, uses;
        }

        private struct Results
        {
            public NativeArray<Matrix4x4> matrices;
            public NativeArray<int> first, count;
            public NativeArray<float3> min, max;
            public NativeList<int> picked;
            public NativeArray<float4> planes;

            public void Dispose()
            {
                if (!planes.IsCreated) return;
                matrices.Dispose();
                first.Dispose();
                count.Dispose();
                min.Dispose();
                max.Dispose();
                picked.Dispose();
                planes.Dispose();
            }
        }

        [BurstCompile]
        private struct Job : IJob
        {
            [ReadOnly] public NativeArray<ItemData> items;
            [ReadOnly] public NativeArray<LevelData> levels;
            [ReadOnly] public NativeArray<PartData> parts;
            [ReadOnly] public NativeArray<int> uses;
            [ReadOnly] public NativeArray<float4> planes;
            public float3 eye, fall;
            public float shadowReach;

            public NativeArray<Matrix4x4> matrices;
            public NativeArray<int> first, count;
            public NativeArray<float3> min, max;
            public NativeList<int> picked;

            public void Execute()
            {
                picked.Clear();
                for (int b = 0; b < count.Length; b++)
                {
                    count[b] = 0;
                    min[b] = float.MaxValue;
                    max[b] = float.MinValue;
                }

                // First pass: which parts, and how many land in each batch.
                for (int i = 0; i < items.Length; i++)
                {
                    var item = items[i];
                    float distance = math.distance(eye, item.centre);
                    int level = 0;
                    while (level < item.levels && distance >= levels[item.firstLevel + level].reach) level++;
                    if (level == item.levels) continue; // too far to draw

                    // Out of view still counts when its shadow can fall into view.
                    if (!InView(item.centre, item.radius)
                        && !(item.casts != 0 && distance - item.radius < shadowReach && ShadowInView(item))) continue;

                    var chosen = levels[item.firstLevel + level];
                    for (int p = chosen.firstPart; p < chosen.firstPart + chosen.parts; p++)
                    {
                        picked.Add(p);
                        var part = parts[p];
                        for (int u = part.firstUse; u < part.firstUse + part.uses; u++) count[uses[u]] = count[uses[u]] + 1;
                    }
                }

                int next = 0;
                for (int b = 0; b < count.Length; b++)
                {
                    first[b] = next;
                    next += count[b];
                    count[b] = 0; // counted again while filling
                }

                // Second pass: the matrices, batch by batch.
                for (int i = 0; i < picked.Length; i++)
                {
                    var part = parts[picked[i]];
                    for (int u = part.firstUse; u < part.firstUse + part.uses; u++)
                    {
                        int b = uses[u];
                        matrices[first[b] + count[b]] = part.matrix;
                        count[b] = count[b] + 1;
                        min[b] = math.min(min[b], part.min);
                        max[b] = math.max(max[b], part.max);
                    }
                }
            }

            private bool InView(float3 centre, float radius)
            {
                for (int i = 0; i < planes.Length; i++)
                    if (math.dot(planes[i].xyz, centre) + planes[i].w < -radius) return false;
                return true;
            }

            // Whether any of a few points down the shadow, as long as the thing is big, is in view.
            private bool ShadowInView(ItemData item)
            {
                for (int step = 1; step <= 3; step++)
                    if (InView(item.centre + fall * (item.radius * step), item.radius)) return true;
                return false;
            }
        }

        private NativeArray<ItemData> items;
        private NativeArray<LevelData> levels;
        private NativeArray<PartData> parts;
        private NativeArray<int> uses;
        private Results shown, filling;
        private JobHandle handle;
        private bool running, ready;
        private int batches;

        // Filled while the scenery is described.
        private readonly List<ItemData> newItems = new List<ItemData>();
        private readonly List<LevelData> newLevels = new List<LevelData>();
        private readonly List<PartData> newParts = new List<PartData>();
        private readonly List<int> newUses = new List<int>();

        public void Begin(int batchCount)
        {
            Release();
            batches = batchCount;
            newItems.Clear();
            newLevels.Clear();
            newParts.Clear();
            newUses.Clear();
        }

        public int Item(Vector3 centre, float radius, bool casts)
        {
            newItems.Add(new ItemData { centre = centre, radius = radius, casts = casts ? 1 : 0, firstLevel = newLevels.Count });
            return newItems.Count - 1;
        }

        public void Level(float reach)
        {
            newLevels.Add(new LevelData { reach = reach, firstPart = newParts.Count });
            var item = newItems[newItems.Count - 1];
            item.levels++;
            newItems[newItems.Count - 1] = item;
        }

        public void Part(Matrix4x4 matrix, Bounds bounds)
        {
            newParts.Add(new PartData { matrix = matrix, min = bounds.min, max = bounds.max, firstUse = newUses.Count });
            var level = newLevels[newLevels.Count - 1];
            level.parts++;
            newLevels[newLevels.Count - 1] = level;
        }

        /// <summary>Leaves one item out from now on, by the place <see cref="Item"/> gave it.</summary>
        public void Drop(int item)
        {
            Collect(); // the job must not be reading
            if (!items.IsCreated || item < 0 || item >= items.Length) return;
            var data = items[item];
            data.levels = 0;
            items[item] = data;
        }

        public void Uses(int batch)
        {
            newUses.Add(batch);
            var part = newParts[newParts.Count - 1];
            part.uses++;
            newParts[newParts.Count - 1] = part;
        }

        public void End()
        {
            items = Copy(newItems);
            levels = Copy(newLevels);
            parts = Copy(newParts);
            uses = Copy(newUses);

            // The most that can be in view at once: each item shows one level only.
            int room = 0, most = 0;
            foreach (var item in newItems)
            {
                int widest = 0, widestParts = 0;
                for (int l = item.firstLevel; l < item.firstLevel + item.levels; l++)
                {
                    int here = 0;
                    for (int p = newLevels[l].firstPart; p < newLevels[l].firstPart + newLevels[l].parts; p++)
                        here += newParts[p].uses;
                    widest = Mathf.Max(widest, here);
                    widestParts = Mathf.Max(widestParts, newLevels[l].parts);
                }
                room += widest;
                most += widestParts;
            }
            shown = Make(room, most);
            filling = Make(room, most);
        }

        /// <summary>
        /// Starts a new pass. With <paramref name="now"/> it is finished before returning, otherwise the next
        /// <see cref="Collect"/> brings it in.
        /// </summary>
        public void Ask(Vector3 eye, Plane[] view, Vector3 fall, float shadowReach, bool now)
        {
            Collect();
            if (!items.IsCreated) return;
            for (int i = 0; i < view.Length; i++)
                filling.planes[i] = new float4(view[i].normal, view[i].distance);
            var job = new Job
            {
                items = items, levels = levels, parts = parts, uses = uses, planes = filling.planes,
                eye = eye, fall = fall, shadowReach = shadowReach,
                matrices = filling.matrices, first = filling.first, count = filling.count,
                min = filling.min, max = filling.max, picked = filling.picked
            };
            running = true;
            if (now)
            {
                job.Run();
                Collect();
            }
            else
            {
                handle = job.Schedule();
                JobHandle.ScheduleBatchedJobs();
            }
        }

        /// <summary>Brings in the pass asked for earlier, waiting for it if need be. False if there was none.</summary>
        public bool Collect()
        {
            if (!running) return false;
            handle.Complete();
            handle = default;
            running = false;
            (shown, filling) = (filling, shown);
            ready = true;
            return true;
        }

        /// <summary>What to draw for one batch. False when none of it is in view.</summary>
        public bool Visible(int batch, out NativeArray<Matrix4x4> matrices, out int first, out int count, out Bounds bounds)
        {
            matrices = shown.matrices;
            first = count = 0;
            bounds = default;
            if (!ready || batch >= batches || shown.count[batch] == 0) return false;
            first = shown.first[batch];
            count = shown.count[batch];
            bounds.SetMinMax(shown.min[batch], shown.max[batch]);
            return true;
        }

        /// <summary>Drops the results, so nothing is drawn until the scenery is described again.</summary>
        public void Forget() => Release();

        public void Dispose() => Release();

        private void Release()
        {
            if (running)
            {
                handle.Complete();
                handle = default;
                running = false;
            }
            ready = false;
            if (items.IsCreated)
            {
                items.Dispose();
                levels.Dispose();
                parts.Dispose();
                uses.Dispose();
            }
            shown.Dispose();
            filling.Dispose();
            shown = default;
            filling = default;
        }

        private Results Make(int room, int most) => new Results
        {
            matrices = new NativeArray<Matrix4x4>(Mathf.Max(1, room), Allocator.Persistent),
            first = new NativeArray<int>(batches, Allocator.Persistent),
            count = new NativeArray<int>(batches, Allocator.Persistent),
            min = new NativeArray<float3>(batches, Allocator.Persistent),
            max = new NativeArray<float3>(batches, Allocator.Persistent),
            picked = new NativeList<int>(Mathf.Max(1, most), Allocator.Persistent),
            planes = new NativeArray<float4>(6, Allocator.Persistent)
        };

        private static NativeArray<T> Copy<T>(List<T> from) where T : struct
        {
            var to = new NativeArray<T>(from.Count, Allocator.Persistent);
            for (int i = 0; i < from.Count; i++) to[i] = from[i];
            return to;
        }
    }
}
