using System.Collections;
using Makeshift.Machines;
using Makeshift.Power;
using UnityEngine;

namespace Makeshift.Dev
{
    /// <summary>
    /// Automated check for the greybox drill. Only runs when the batch runner asks
    /// (-makeshiftSmoke on the command line). Logs PASS/FAIL lines and exits the editor.
    /// </summary>
    public class GreyboxSmokeTest : MonoBehaviour
    {
        public Machine machine;
        public PartSocket motorSocket;
        public PartSocket handleSocket;
        public MachinePart toaster;
        public MachinePart stick;
        public PowerPlug plug;
        public PowerPort port;
        public OreNode rock;
        public SmokeSuite[] suites;

        private int failures;

        private void Start()
        {
#if UNITY_EDITOR
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-makeshiftSmoke") < 0) return;
            StartCoroutine(Run());
#endif
        }

        private void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[SMOKE] {(ok ? "PASS" : "FAIL")}: {what}");
        }

        private IEnumerator Run()
        {
            yield return new WaitForSeconds(0.5f);

            // Wrong part in the motor socket: toaster as motor.
            motorSocket.Insert(toaster);

            // Handle goes in the way a player does it: dropped into the trigger, let go, wait for the snap.
            var stickBody = stick.GetComponent<Rigidbody>();
            stickBody.useGravity = false;
            stickBody.linearVelocity = Vector3.zero;
            stick.transform.position = handleSocket.transform.position;
            stickBody.position = handleSocket.transform.position; // interpolated bodies ignore transform-only teleports
            yield return new WaitForSeconds(1f);
            Check(stick.IsSocketed, "stick snaps into the socket after hovering (trigger path + Auto Hand bridge)");
            if (!stick.IsSocketed) handleSocket.Insert(stick);
            Check(machine.IsAssembled, "machine assembled with both sockets filled");

            int grabbableLayer = LayerMask.NameToLayer("Grabbable");
            Check(grabbableLayer >= 0 && !Physics.GetIgnoreLayerCollision(grabbableLayer, motorSocket.gameObject.layer),
                "Grabbable layer can touch socket layer");
            Check(!motorSocket.IsIdeal(toaster), "toaster is a wrong part for the motor socket");
            Check(machine.Reliability < 0.6f, $"wrong parts lower reliability ({machine.Reliability:0.00})");

            // Plug the battery in by dropping the plug onto the port.
            plug.transform.position = port.transform.position;
            plug.GetComponent<Rigidbody>().position = port.transform.position;
            var plugBody = plug.GetComponent<Rigidbody>();
            plugBody.useGravity = false; // stays put like a held plug
            plugBody.linearVelocity = Vector3.zero;
            yield return new WaitForSeconds(0.5f);
            Check(port.IsConnected, $"plug connects to port (quality {port.Quality:0.00})");
            if (!port.IsConnected)
                Debug.Log($"[SMOKE] debug plug={plug.transform.position} port={port.transform.position} " +
                          $"plugSource={(plug.Source != null)} plugPort={plug.ConnectedPort} " +
                          $"kinematic={plugBody.isKinematic} layer={plug.gameObject.layer} portLayer={port.gameObject.layer}");

            // Switch on before it would be ready is fine; run for a while, sped up.
            machine.TurnOn();
            Time.timeScale = 4f;
            int chunksBefore = rock.ChunksLeft;
            bool sawRunning = false, sawFailure = false;
            for (float t = 0f; t < 90f; t += Time.deltaTime) // game seconds
            {
                if (machine.State == MachineState.Running) sawRunning = true;
                if (machine.State == MachineState.Jammed || machine.State == MachineState.Sputtering ||
                    machine.State == MachineState.Overheated) sawFailure = true;
                // Backfires can eject parts and blast the plug loose: put things back, like a player would.
                if (!machine.IsAssembled)
                {
                    if (!motorSocket.IsFilled && !toaster.IsSocketed) motorSocket.Insert(toaster);
                    if (!handleSocket.IsFilled && !stick.IsSocketed) handleSocket.Insert(stick);
                }
                if (!port.IsConnected)
                {
                    plugBody.position = port.transform.position;
                    plugBody.linearVelocity = Vector3.zero;
                }
                // Keep it going: bonk jams.
                if (machine.State == MachineState.Jammed) machine.Bonk();
                yield return null;
            }
            Time.timeScale = 1f;
            Check(sawRunning, "machine reached Running");
            Check(sawFailure, "machine failed at least once (it works, badly)");
            Check(rock.ChunksLeft < chunksBefore, $"drill dug ore ({chunksBefore - rock.ChunksLeft} chunks)");

            // Backfire then bonk back to life.
            machine.Backfire();
            Check(machine.State == MachineState.Jammed, "backfire jams the machine");
            for (int i = 0; i < 30 && machine.State == MachineState.Jammed; i++) machine.Bonk();
            Check(machine.State != MachineState.Jammed, "bonking fixes a jam eventually");

            // Player pulls the stick back out: simulate Auto Hand's grab event.
            if (!stick.IsSocketed) handleSocket.Insert(stick);
            var stickGrab = stick.GetComponent<Autohand.Grabbable>();
            Check(stick.GetComponent<Makeshift.Integration.AutoHandPartHook>() != null, "bridge added the grab hook to the part");
            stickGrab.OnGrabEvent.Invoke(null, stickGrab);
            Check(!stick.IsSocketed && !handleSocket.IsFilled, "grabbing a socketed part frees the socket");
            Check(!machine.IsAssembled, "machine knows it lost a part");

            if (suites != null)
            {
                foreach (var suite in suites)
                {
                    yield return suite.Run();
                    failures += suite.Failures;
                }
            }

            Debug.Log($"[SMOKE] DONE failures={failures}");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(failures == 0 ? 0 : 1);
#endif
        }
    }
}
