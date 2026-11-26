using System;
using UnityEngine;

namespace Makeshift.Core
{
    /// <summary>
    /// Shared questions for gameplay code about the hand system. AutoHandBridge fills these in.
    /// When they are null (tests, no hands), nothing is held and nothing counts as a player body.
    /// </summary>
    public static class HandCheck
    {
        /// <summary>Is this body in a player's hand right now?</summary>
        public static Func<Rigidbody, bool> IsHeld;

        /// <summary>Is this body part of the player (a hand or the head)? Crafting glue ignores these.</summary>
        public static Func<Rigidbody, bool> IsPlayerBody;

        /// <summary>Makes any hands let go of this body.</summary>
        public static Action<Rigidbody> ForceRelease;

        /// <summary>Where the hand holding this body is, or null if nothing holds it.</summary>
        public static Func<Rigidbody, Vector3?> HoldPoint;

        public static bool Held(Rigidbody body)
        {
            return body != null && IsHeld != null && IsHeld(body);
        }

        public static bool PlayerBody(Rigidbody body)
        {
            return body != null && IsPlayerBody != null && IsPlayerBody(body);
        }

        public static void Release(Rigidbody body)
        {
            if (body != null) ForceRelease?.Invoke(body);
        }
    }
}
