# Regression checks

Run the persistence checks on Windows with the .NET 10 SDK:

```powershell
dotnet run --project test/WorkbenchChecks/WorkbenchChecks.csproj
```

These exercise the production JSON formatter and storage/workspace services. Formatting checks cover Unicode, large numbers, duplicate properties, invalid JSON, and repeated formatting. Persistence checks use a temporary directory and cover failed writes, failed replacement, unreadable or invalid drafts, and reopening an empty workspace.

After deploying the app, check repeated launches:

```powershell
./test/Verify-SingleInstance.ps1
```

The script leaves the app open. Use `-PackageName` to target an isolated test package.

Check these UI paths with a disposable workspace:

- Select a card, duplicate it, and select the original again. The sidebar selection and editor must agree throughout.
- Create, move, archive, and delete the active card; then filter and clear the navigation search. Selection must follow the visible active card without changing the selected card during a refresh.
- Open Settings or Archive, then create a card. The new card must be selected and the previous page must remain reachable.
- Make the draft destination unwritable and close the window. An error must appear and the window must stay open. Restore write access and retry closing.
- Request close twice while a save is pending. The second request must not close the window before the save finishes.
- Focus each JSON editor, press F5, and check that the render timestamp changes without altering the text. Press Ctrl+S and check that the save status updates. Repeat with the preview maximized.
- Format compact JSON in each editor, then undo once. The exact original text must return. Invalid JSON must remain unchanged and produce a formatting error in Problems.
- Select Expanded JSON and confirm template expressions are resolved using the sample data. Confirm the text can be selected/copied but not edited. An invalid template must clear the previous result.
- Render a card with an unsupported element and a valid fallback. Its parser warning must appear in Problems even when rendering succeeds.
- Fill in card inputs and invoke Action.Submit. Output must show the action data and entered inputs. Action.OpenUrl must display the action URL without navigating away. Change cards and verify actions still refer to the new preview.
- Keep typing while saving. A save of older content must not change the newer edit's status to saved; the next autosave must save the latest text.
- Tab through the format buttons, diagnostics tabs, and expanded JSON. Check light/dark themes, high contrast, and 125%, 150%, and 200% scaling for readable text and reachable controls.
