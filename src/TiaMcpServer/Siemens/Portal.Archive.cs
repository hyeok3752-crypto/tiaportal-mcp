using Siemens.Engineering;
using System;
using System.IO;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Project archive (Openness manual V19, 5.3.4): Project.Archive writes the saved project as a
        // .zapXX file that TIA Portal can retrieve, e.g. as a backup before an import. Archive does
        // not change the project or its location (unlike SaveAs), and TIA refuses it while the
        // project has unsaved changes - checked first so the message says so.

        public FileInfo ArchiveProject(string targetDirectory, string archiveName, ProjectArchivationMode mode)
        {
            return Operation.Run(_logger, nameof(ArchiveProject), PortalErrorCode.InvalidState,
                () =>
                {
                    if (IsProjectNull() || _project is not Project project)
                    {
                        throw new PortalException(PortalErrorCode.InvalidState,
                            "No local project is open in TIA Portal (a multiuser session cannot be archived here).");
                    }

                    Operation.RequireAbsolutePath(targetDirectory, nameof(targetDirectory));

                    if (project.IsModified)
                    {
                        throw new PortalException(PortalErrorCode.InvalidState,
                            "The project has unsaved changes, and TIA Portal only archives the saved state. Save it first ('SaveProject'), or discard the changes in TIA Portal.");
                    }

                    if (string.IsNullOrWhiteSpace(archiveName))
                    {
                        // '<project>_<yyyyMMdd_HHmm>.zap20' for a V20 project (.ap20), so TIA Portal's
                        // own Retrieve dialog recognizes it.
                        var extension = project.Path?.Extension ?? string.Empty;
                        var archiveExtension = extension.StartsWith(".ap", StringComparison.OrdinalIgnoreCase)
                            ? ".zap" + extension.Substring(3)
                            : ".zap";

                        archiveName = $"{project.Name}_{DateTime.Now:yyyyMMdd_HHmm}{archiveExtension}";
                    }

                    archiveName = FileNames.Safe(archiveName);

                    var directory = new DirectoryInfo(targetDirectory);
                    if (!directory.Exists)
                    {
                        directory.Create();
                    }

                    var target = new FileInfo(Path.Combine(directory.FullName, archiveName));
                    if (target.Exists)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"'{target.FullName}' already exists. Pick another 'archiveName' or folder.");
                    }

                    project.Archive(directory, archiveName, mode);

                    target.Refresh();
                    return target;
                },
                ("targetDirectory", targetDirectory), ("archiveName", archiveName));
        }
    }
}
