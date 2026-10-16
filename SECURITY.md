# Security policy

## Supported versions

Only the [latest release](https://github.com/wbgcoding/Aura-Toggle/releases/latest) gets fixes.
If you run an older version, please check whether the problem is still there in the latest one
before reporting it.

## Reporting a vulnerability

Please do not open a public issue for a security problem. Report it privately through
[GitHub's private vulnerability reporting](https://github.com/wbgcoding/Aura-Toggle/security/advisories/new)
instead. Include the version, whether you use the portable exe or the setup, and the steps that
show the problem.

You will get an answer within a week. Once a fix is out, the advisory is published with credit
to you, unless you would rather stay unnamed.

## What is in scope

Aura Toggle has no network code, no account and no server. Relevant reports are things like:

- a way to make the exe or the setup load or run code it should not (DLL search order, files in
  its folders, the .NET runtime download in the setup)
- a stored file in `%LOCALAPPDATA%\aura-toggle` that, when crafted, crashes the tool or makes it
  do something other than switch the lighting
- data written to the log or the error dialog that should not be there
- anything that writes to the controller beyond what the README describes

Problems in ASUS firmware, drivers or Armoury Crate are out of scope; please report those to ASUS.
