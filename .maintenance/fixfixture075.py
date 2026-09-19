from pathlib import Path
p = Path('src/TaskbarTiles/UiReliabilityTests.cs')
s = p.read_text(encoding='utf-8-sig')
old = 'if (WindowNative.ProcessId(h) == pid && Native.IsWindowVisible(h) && Native.GetWindow(h, 4) == IntPtr.Zero) { found = h; return false; }'
new = 'if (WindowNative.ProcessId(h) == pid && Native.IsWindowVisible(h)) { var title = new StringBuilder(100); Native.GetWindowText(h, title, title.Capacity); if (title.ToString().StartsWith("TT075 clicks ", StringComparison.Ordinal)) { found = h; return false; } }'
assert s.count(old) == 1
p.write_text(s.replace(old, new), encoding='utf-8', newline='\n')
print('Fixture discovery handles WinForms hidden owner for ShowInTaskbar=false; production matching is unchanged.')
