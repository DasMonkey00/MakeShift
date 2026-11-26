# MCP servers are off by default

The Unity MCP server (MCP for Unity, `UnityMCP` tools) is **off by default** in this project.

- Do not start, use, or rely on any MCP server tool (`mcp__UnityMCP__*` or similar) unless the user has
  explicitly turned it on in this conversation.
- If a task needs MCP access, ask the user to turn it on first. Say which server and why, then wait.
- Do not treat an open MCP window or tab as permission to use it.
- **Standing permission (user-granted):** Claude may open the MCP For Unity window, click **Start Server**,
  do the task, then click **Stop Server** and close the MCP window. This is allowed only under that full
  cycle. Never leave the server running or the window open after the task. If the cycle cannot be
  completed (for example, a failed stop or close), tell the user right away.
- Opening the window and clicking its buttons needs desktop control (computer-use). Claude must request
  access for the Unity editor and wait for the user to approve it. Claude must not approve it for the user.
- If the user has turned MCP on, use it only for the task they asked for.

**Why:** the MCP For Unity window, even when open with its local server stopped, probes 127.0.0.1:8222 on
the main thread every 2 seconds and freezes the editor for about 250 ms. This causes the VR stutter seen in
testing (see CLAUDE.md, "Known issue: 2-second VR stutter"). Leaving MCP on or its tab open breaks the
90 fps test runs.

**How to apply:** before any Unity MCP call, check whether the user has said MCP is on for this session.
If not, stop and ask. When running the game or VR preview, assume the MCP window must stay closed unless
the user says otherwise.
