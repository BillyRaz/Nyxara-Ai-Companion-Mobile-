# Safe Export Steps

1. Open the project in Unity.
2. Let scripts compile.
3. Open `Nyxara AI > Studio` and confirm the project still behaves correctly.
4. In the Project window, select only the paths listed in `Release/UNITYPACKAGE_INCLUDE_LIST.txt`.
5. Double-check that nothing from `Release/UNITYPACKAGE_EXCLUDE_LIST.txt` is selected.
6. Export the package.
7. Import the package into a clean test project.
8. Verify:
   - `Nyxara AI > Studio` opens
   - the editor windows appear under the expected menu
   - the Nyxara demo content resolves correctly
   - no excluded third-party/demo content was pulled in by accident
9. If a selected prefab pulls in an excluded dependency, remove that prefab from the package and re-export.

## Important

This release kit avoids moving project folders because that could break the current working setup.

If you want a second pass later, we can create a true staged export copy or a clean package-only asset root, but that should be done carefully and separately from the live project.
