from pathlib import Path
p=Path('src/TaskbarTiles/StreamDockModule.cs')
s=p.read_text(encoding='utf-8-sig')
def one(old,new):
    global s
    assert s.count(old)==1, ('unexpected source match',old,s.count(old))
    s=s.replace(old,new)
one('sealed class DockManager\n','sealed partial class DockManager\n')
start=s.index('            var j = Decode<DockJournal>',s.index('void RecoverInterruptedChange'))
end=s.index('            File.Delete(file);',start)
s=s[:start]+'''            var j = Decode<DockJournal>(ReadBounded(file, 65536));
            RollbackJournal(j);
'''+s[end:]
one('if (plan.Enabled.Length != 0) {\n                                Directory.CreateDirectory(stage);', 'if (plan.Enabled.Length != 0 || legacyMoves.Length != 0) {\n                                Directory.CreateDirectory(stage);')
one('                                Unpack(p, stage);\n                                var manifest', '''                                Unpack(p, stage);
                                ImportLegacyWorkers(p, stage, source);
                                // Private files survive; bundled executable/manifests always win.
                                Unpack(p, stage);
                                var manifest''')
one('Directory.Move(old, oldBackup); movedLegacy.Add(legacy);','Directory.Move(old, oldBackup); movedLegacy.Add(legacy); if (AfterLegacyMoved != null) AfterLegacyMoved();')
one('                            else if (movedOld) {','                            else if (movedOld || Directory.Exists(stage)) {')
one('                                CopySafe(backup, disabled);','                                if (Directory.Exists(stage)) Directory.Move(stage, disabled); else CopySafe(backup, disabled);')
start=s.index('                            // Roll back this package.')
end=s.index('                            throw;',start)
s=s[:start]+'''                            // Restore all old folders together, including a partly promoted replacement.
                            if (File.Exists(Path.Combine(Store, "pending-package.json"))) RecoverInterruptedChange();
'''+s[end:]
p.write_text(s,encoding='utf-8')
p=Path('src/TaskbarTiles/AssemblyInfo.cs');s=p.read_text();s=s.replace('AssemblyInformationalVersion("0.9.1")','AssemblyInformationalVersion("0.10.1")');p.write_text(s)
p=Path('src/TaskbarTiles/TaskbarTiles.csproj');s=p.read_text();anchor='    <Compile Include="StreamDockModule.cs" />';assert s.count(anchor)==1;s=s.replace(anchor,anchor+'\n    <Compile Include="StreamDockMigration.cs" />\n    <Compile Include="StreamDockMigrationTests.cs" />');p.write_text(s)
p=Path('src/TaskbarTiles/StreamDockTests.cs');s=p.read_text();anchor='                    Check(real.Catalog.Packages.Length == 1';assert s.count(anchor)==1;s=s.replace(anchor,'                    passed += StreamDockMigrationTests.Run(tmp);\n'+anchor);p.write_text(s)
