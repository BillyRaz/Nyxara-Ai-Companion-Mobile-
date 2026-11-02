# Nyxara AI Studio Release Kit

This folder is a non-destructive packaging guide for building a release-safe package from the current project.

No risky asset moves were applied to the live Unity project from this release kit.

Use the files here to package only the approved Nyxara AI Studio content while keeping external, third-party, local-only, and license-unclear content out of the release.

## Files In This Folder

- `PACKAGE_STRUCTURE.md`
- `UNITYPACKAGE_INCLUDE_LIST.txt`
- `UNITYPACKAGE_EXCLUDE_LIST.txt`
- `MANUAL_REVIEW_ITEMS.txt`
- `SAFE_DEMO_CONTENT.txt`
- `EXPORT_STEPS.md`

## Safe Approach

- Keep the current working project layout intact.
- Export only the include list.
- Do not move or rename live project assets unless you explicitly want a second cleanup pass later.
