# Robocopy GUI

A simple GUI wrapper for people who use `robocopy.exe`. I wanted something like this for a long time, but the projects I tried were never quite what I wanted.

Pick a source and destination, choose what to copy, review the generated command, and hit **Start**. You still get the full power of Robocopy, without having to build the command by hand.

### What it solves

* **No more remembering Robocopy syntax** - common options are exposed as simple controls.
* **Still gives you the real command** - the command is visible and editable, so there is no hidden magic.
* **Doesn't get in your way** - options the GUI doesn't support are preserved, so you can still use most of Robocopy switches.
* **Easy folder selection** - select or exclude subfolders without manually writing `/XD` paths.
* **Run unattended** - schedule a start/stop time and let Robocopy do its thing.
* **Know what happened** - live Robocopy output, per-run logs, and clear exit-code status.
* **Quick byte-size check** - optionally compare the total source and destination file sizes after a successful copy.
* **Repeatable jobs** - save your job settings and import them again when needed.
* **No extra infrastructure** - a single lightweight executable with no third-party dependencies.

## Requirements

* Windows
* .NET Framework 4.6.2

## A few details worth knowing

* The GUI runs `robocopy.exe` as the current user and never elevates.
* Scheduled jobs require the application to remain open.
* Verification checks file sizes, not file contents or hashes.

That's about it. If you know Robocopy, you should be able to open the app and understand what it's doing immediately.