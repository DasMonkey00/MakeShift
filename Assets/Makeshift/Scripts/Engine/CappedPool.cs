using System;
using System.Collections.Generic;
using UnityEngine;

namespace Makeshift.Engine
{
    /// <summary>
    /// At most <c>cap</c> short-lived objects (wood chunks, debris) alive at once. A spent object is reused; when
    /// all are in use, the oldest is taken back. Keeps rigidbody and draw counts bounded however much the players
    /// smash.
    /// </summary>
    public sealed class CappedPool
    {
        private readonly int cap;
        private readonly Func<GameObject> create;
        private readonly List<GameObject> items = new List<GameObject>(); // oldest first

        public CappedPool(int cap, Func<GameObject> create)
        {
            this.cap = Mathf.Max(1, cap);
            this.create = create;
        }

        public int Count => items.Count;

        /// <summary>
        /// An object ready to place, still switched off: position it, then switch it on. It disappears again after
        /// <paramref name="seconds"/>.
        /// </summary>
        public GameObject Take(float seconds)
        {
            items.RemoveAll(o => o == null); // destroyed with their scene
            int index = items.FindIndex(o => !o.activeSelf);
            if (index < 0 && items.Count >= cap) index = 0;

            GameObject item;
            if (index < 0) item = create();
            else
            {
                item = items[index];
                items.RemoveAt(index);
            }
            items.Add(item);
            item.SetActive(false);

            var expiry = item.GetComponent<PoolExpiry>();
            if (expiry == null) expiry = item.AddComponent<PoolExpiry>();
            expiry.Seconds = seconds;
            return item;
        }
    }
}
