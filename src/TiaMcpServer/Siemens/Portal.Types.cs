using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.Multiuser;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks.Interface;
using Siemens.Engineering.SW.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        #region types

        public PlcType? GetType(string softwarePath, string typePath)
        {
            _logger?.LogInformation($"Getting type by path: {typePath}");

            if (IsProjectNull())
            {
                return null;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is PlcSoftware plcSoftware)
            {
                var typeGroup = plcSoftware?.TypeGroup;

                if (typeGroup != null)
                {
                    var path = typePath.Contains("/") ? typePath.Substring(0, typePath.LastIndexOf("/")) : string.Empty;
                    var regexName = typePath.Contains("/") ? typePath.Substring(typePath.LastIndexOf("/") + 1) : typePath;

                    PlcType? type = null;

                    var group = GetPlcTypeGroupByPath(softwarePath, path);
                    if (group is not null)
                    {
                        // Exact name first, for the same reason as GetBlock: names with '[ ]'
                        // must resolve without regex escaping.
                        type = group.Types.Find(regexName);

                        if (type is not null)
                        {
                            return type;
                        }

                        if (regexName.IndexOfAny(_regexChars) >= 0)
                        {
                            try
                            {
                                var regex = new Regex(regexName, RegexOptions.IgnoreCase);
                                type = group.Types.FirstOrDefault(t => regex.IsMatch(t.Name)) as PlcType;
                            }
                            catch (Exception)
                            {
                                // Invalid regex, return null
                                return null;
                            }
                        }
                        else
                        {
                            type = group.Types.FirstOrDefault(t => t.Name.Equals(regexName, StringComparison.OrdinalIgnoreCase));
                        }

                        return type;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Root-relative path of a PLC data type, the GetBlockPath counterpart.
        /// </summary>
        public string GetTypePath(PlcType type)
        {
            if (type == null)
            {
                return string.Empty;
            }

            if (type.Parent is PlcTypeGroup parentGroup)
            {
                var groupPath = GetPlcTypeGroupPath(parentGroup, includeSystemRoot: false);
                return string.IsNullOrEmpty(groupPath) ? type.Name : $"{groupPath}/{type.Name}";
            }

            return type.Name;
        }

        public List<PlcType> GetTypes(string softwarePath, string regexName = "")
        {
            _logger?.LogInformation("Getting types...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<PlcType>();

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is PlcSoftware plcSoftware)
                {
                    var group = plcSoftware?.TypeGroup;

                    if (group != null)
                    {
                        GetTypesRecursive(group, list, regexName);
                    }
                }
            }
            catch (Exception)
            {
                // Console.WriteLine($"Error getting user defined types: {ex.Message}");
            }

            return list;
        }

        public PlcType? ExportXmlType(string softwarePath, string typePath, string exportPath, bool preservePath = false)
        {
            _logger?.LogInformation($"Exporting type by path: {typePath}");

            try
            {
                if (IsProjectNull())
                {
                    throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
                }

                Operation.RequireAbsolutePath(exportPath);

                var type = GetType(softwarePath, typePath);

                if (type == null)
                {
                    throw new PortalException(PortalErrorCode.NotFound, "Type not found");
                }

                // TIA Portal never exports inconsistent types
                if (!type.IsConsistent)
                {
                    throw new PortalException(PortalErrorCode.InvalidState, "Type is inconsistent; TIA Portal does not export inconsistent types.");
                }

                if (preservePath)
                {
                    var groupPath = "";
                    if (type.Parent is PlcTypeGroup parentGroup)
                    {
                        groupPath = GetPlcTypeGroupPath(parentGroup);
                    }

                    exportPath = Path.Combine(exportPath, FileNames.SafeRelative(groupPath), $"{FileNames.Safe(type.Name)}.xml");
                }
                else
                {
                    exportPath = Path.Combine(exportPath, $"{FileNames.Safe(type.Name)}.xml");
                }

                if (File.Exists(exportPath))
                {
                    File.Delete(exportPath);
                }

                type.Export(new FileInfo(exportPath), ExportOptions.None);

                return type;
            }
            catch (Exception ex)
            {
                var pex = ex as PortalException ?? new PortalException(PortalErrorCode.ExportFailed, "Export failed", null, ex);

                if (!pex.Data.Contains("softwarePath")) pex.Data["softwarePath"] = softwarePath;
                if (!pex.Data.Contains("typePath")) pex.Data["typePath"] = typePath;
                if (!pex.Data.Contains("exportPath")) pex.Data["exportPath"] = exportPath;

                _logger?.LogError(pex, "ExportXmlType failed for {SoftwarePath} {TypePath} -> {ExportPath}", softwarePath, typePath, exportPath);
                throw pex;
            }
        }

        public bool ImportXmlType(string softwarePath, string groupPath, string importPath)
        {
            _logger?.LogInformation($"Importing type from path: {importPath}");

            // This used to swallow every failure into 'false', so the tool could only say
            // "Failed importing" - live, a servo FB with 900+ elements in one network was
            // rejected by TIA and the reason never reached the caller. Each failure now throws
            // with its own reason, including TIA's message from the import itself.
            return Operation.Run(_logger, nameof(ImportXmlType), PortalErrorCode.ImportFailed,
                () =>
                {
                    if (IsProjectNull())
                    {
                        throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal.");
                    }

                    Operation.RequireAbsolutePath(importPath, nameof(importPath));

                    var fileInfo = new FileInfo(importPath);

                    if (!fileInfo.Exists)
                    {
                        throw new PortalException(PortalErrorCode.NotFound, $"Import file not found: '{importPath}'.");
                    }

                    if (GetSoftwareContainer(softwarePath)?.Software is not PlcSoftware)
                    {
                        throw new PortalException(PortalErrorCode.NotFound,
                            $"No PLC software found at '{softwarePath}'. 'GetProjectTree' lists the valid software paths.");
                    }

                    var group = GetPlcTypeGroupByPath(softwarePath, groupPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Group not found at '{groupPath}'. 'GetSoftwareTree' lists the valid group paths.");

                    RefuseLibraryTypeInstance(softwarePath, importPath);

                    IList<PlcType> list;

                    try
                    {
                        list = group.Types.Import(fileInfo, ImportOptions.Override);
                    }
                    catch (Exception ex)
                    {
                        throw new PortalException(PortalErrorCode.ImportFailed,
                            $"TIA Portal rejected the PLC data type import from '{importPath}': {Operation.DescribeChain(ex)}" +
                            (OnlineHint(GetSoftwareContainer(softwarePath)) ?? string.Empty), null, ex);
                    }

                    if (list == null || list.Count == 0)
                    {
                        throw new PortalException(PortalErrorCode.ImportFailed,
                            $"TIA Portal imported nothing from '{importPath}' and gave no reason. Check that the file is a SimaticML PLC data type export.");
                    }

                    return true;
                },
                ("softwarePath", softwarePath), ("groupPath", groupPath), ("importPath", importPath));
        }

        /// <summary>
        /// REQUESTS.md P11-4: importing over an object that is an instance of a project library type
        /// makes TIA Portal open a modal "The test environment of the library cannot be deleted or
        /// replaced" dialog, which blocks Openness until someone clicks OK. Refuse up front instead,
        /// when the file names an existing block or type that carries a library type link.
        /// </summary>
        private void RefuseLibraryTypeInstance(string softwarePath, string importPath)
        {
            var (kind, name) = ReadSimaticMlObjectName(importPath);

            if (name == null || GetSoftwareContainer(softwarePath)?.Software is not PlcSoftware plc)
            {
                return;
            }

            IEngineeringServiceProvider? existing = kind == "type"
                ? FindTypeAnywhere(plc.TypeGroup, name)
                : FindBlockAnywhere(plc.BlockGroup, name);

            var info = existing?.GetService<LibraryTypeInstanceInfo>();

            if (info == null)
            {
                return;
            }

            var version = info.LibraryTypeVersion;
            var library = version == null
                ? "a project library type"
                : $"library type '{version.TypeObject?.Name}' V{version.VersionNumber} ({version.State})";

            throw new PortalException(PortalErrorCode.InvalidState,
                $"'{name}' is an instance of {library}. TIA Portal does not let an import replace it (it opens a " +
                "'test environment of the library cannot be deleted or replaced' dialog). Change it through the project " +
                "library instead (edit the type, then release the version), or import under a different name.");
        }

        /// <summary>("type" or "block", name) of the first object in a SimaticML file; (null, null) if unreadable.</summary>
        private static (string? Kind, string? Name) ReadSimaticMlObjectName(string importPath)
        {
            try
            {
                var doc = System.Xml.Linq.XDocument.Load(importPath);

                foreach (var element in doc.Descendants())
                {
                    var local = element.Name.LocalName;
                    var kind = local.StartsWith("SW.Types.", StringComparison.Ordinal) ? "type"
                        : local.StartsWith("SW.Blocks.", StringComparison.Ordinal) && !local.StartsWith("SW.Blocks.CompileUnit", StringComparison.Ordinal) ? "block"
                        : null;

                    if (kind == null)
                    {
                        continue;
                    }

                    var name = element.Elements().FirstOrDefault(e => e.Name.LocalName == "AttributeList")?
                        .Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value;

                    return (kind, string.IsNullOrWhiteSpace(name) ? null : name);
                }
            }
            catch (Exception)
            {
                // Not readable as XML here: let TIA Portal's own import report the problem.
            }

            return (null, null);
        }

        private static PlcType? FindTypeAnywhere(PlcTypeGroup group, string name)
        {
            return group.Types.Find(name)
                ?? group.Groups.Select(g => FindTypeAnywhere(g, name)).FirstOrDefault(t => t != null);
        }

        private static global::Siemens.Engineering.SW.Blocks.PlcBlock? FindBlockAnywhere(global::Siemens.Engineering.SW.Blocks.PlcBlockGroup group, string name)
        {
            return group.Blocks.Find(name)
                ?? group.Groups.Select(g => FindBlockAnywhere(g, name)).FirstOrDefault(b => b != null);
        }

        public (List<PlcType> Exported, List<string> Failures) ExportXmlTypes(string softwarePath, string exportPath, string regexName = "", bool preservePath = false)
        {
            _logger?.LogInformation("Exporting types...");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            Operation.RequireAbsolutePath(exportPath);

            var exportList = new List<PlcType>();
            var failures = new List<string>();

            PlcType[] list;

            try
            {
                list = GetTypes(softwarePath, regexName).ToArray();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to retrieve type list for {SoftwarePath}", softwarePath);
                return (exportList, failures);
            }

            for (int i = 0; i < list.Count(); i++)
            {
                var type = list[i];

                _logger?.LogDebug("- Exporting type {Index}/{Total} : {Name}", i, list.Count(), type.Name);

                string path;
                if (preservePath)
                {
                    var groupPath = "";
                    if (type.Parent is PlcTypeGroup parentGroup)
                    {
                        groupPath = GetPlcTypeGroupPath(parentGroup);
                    }
                    path = Path.Combine(exportPath, FileNames.SafeRelative(groupPath), $"{FileNames.Safe(type.Name)}.xml");
                }
                else
                {
                    path = Path.Combine(exportPath, $"{FileNames.Safe(type.Name)}.xml");
                }

                try
                {
                    if (!type.IsConsistent)
                    {
                        _logger?.LogWarning("Skipping inconsistent type {Name}", type.Name);
                        continue;
                    }

                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    if (File.Exists(path))
                    {
                        try
                        {
                            File.Delete(path);
                        }
                        catch (Exception ioEx)
                        {
                            failures.Add($"{type.Name}: cannot delete existing file ({ioEx.Message})");
                            _logger?.LogError(ioEx, "Delete failed for {File}", path);
                            continue;
                        }
                    }

                    try
                    {
                        type.Export(new FileInfo(path), ExportOptions.None);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{type.Name}: export failed ({ex.Message})");
                        _logger?.LogError(ex, "Export failed for type {Type}", type.Name);
                        continue;
                    }

                    exportList.Add(type);
                }
                catch (Exception ex)
                {
                    failures.Add($"{type.Name}: unexpected exception ({ex.Message})");
                    _logger?.LogError(ex, "Unexpected error at type {Type}", type.Name);
                }
            }

            if (failures.Count > 0)
            {
                _logger?.LogWarning($"ExportXmlTypes completed with {failures.Count} failures out of {list.Count()}. First failure: {failures[0]}");
            }
            else
            {
                _logger?.LogInformation($"ExportXmlTypes completed successfully. Exported {exportList.Count} types.");
            }

            return (exportList, failures);
        }

        #endregion

        #region get...by path

        private PlcTypeGroup? GetPlcTypeGroupByPath(string softwarePath, string groupPath)
        {
            if (_project == null)
            {
                return null;
            }

            var plcSoftware = (GetSoftwareContainer(softwarePath)?.Software as PlcSoftware);

            return plcSoftware?.TypeGroup == null
                ? null
                : WalkGroups<PlcTypeGroup>(plcSoftware.TypeGroup, groupPath, TypeSubgroups, g => g.Name);
        }

        /// <param name="includeSystemRoot">See GetPlcBlockGroupPath.</param>
        private string GetPlcTypeGroupPath(PlcTypeGroup group, bool includeSystemRoot = true)
        {
            return BuildGroupPath<PlcTypeGroup>(
                group,
                g => g.Parent as PlcTypeGroup,
                g => g.Name,
                g => g is PlcTypeSystemGroup,
                includeSystemRoot);
        }

        #endregion

        #region get recursive ...

        private bool GetTypesRecursive(PlcTypeGroup group, List<PlcType> list, string regexName = "")
        {
            var before = list.Count;

            WalkRecursive<PlcTypeGroup, PlcType>(group, list, g => g.Types, TypeSubgroups, t => t.Name, regexName);

            return list.Count > before;
        }

        #endregion

        #region groups

        public PlcTypeUserGroup CreateTypeGroup(string softwarePath, string parentGroupPath, string name)
        {
            return Operation.Run(_logger, nameof(CreateTypeGroup), PortalErrorCode.CreateFailed,
                () =>
                {
                    EnsureValidName(name);

                    var parent = GetPlcTypeGroupByPath(softwarePath, parentGroupPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Type group not found at '{parentGroupPath}'.");

                    return parent.Groups.Create(name);
                },
                ("softwarePath", softwarePath), ("parentGroupPath", parentGroupPath), ("name", name));
        }

        public bool DeleteTypeGroup(string softwarePath, string groupPath)
        {
            return Operation.Run(_logger, nameof(DeleteTypeGroup), PortalErrorCode.DeleteFailed,
                () =>
                {
                    EnsureUserGroup(GetPlcTypeGroupByPath(softwarePath, groupPath), groupPath).Delete();
                    return true;
                },
                ("softwarePath", softwarePath), ("groupPath", groupPath));
        }

        #endregion

        #region types (delete, rename)

        public bool DeleteType(string softwarePath, string typePath)
        {
            return Operation.Run(_logger, nameof(DeleteType), PortalErrorCode.DeleteFailed,
                () =>
                {
                    var type = GetType(softwarePath, typePath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Type not found at '{typePath}'. Use 'GetTypes' to list the available types.");

                    EnsureNotKnowHowProtected(type);
                    type.Delete();

                    return true;
                },
                ("softwarePath", softwarePath), ("typePath", typePath));
        }

        public bool RenameType(string softwarePath, string typePath, string newName)
        {
            return Operation.Run(_logger, nameof(RenameType), PortalErrorCode.RenameFailed,
                () =>
                {
                    EnsureValidName(newName);

                    var type = GetType(softwarePath, typePath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Type not found at '{typePath}'. Use 'GetTypes' to list the available types.");

                    EnsureNotKnowHowProtected(type);
                    type.Name = newName;

                    return true;
                },
                ("softwarePath", softwarePath), ("typePath", typePath), ("newName", newName));
        }

        #endregion

        #region types

        public PlcType CopyType(string softwarePath, string typePath, string targetGroupPath, bool overwrite = false)
        {
            return Operation.Run(_logger, nameof(CopyType), PortalErrorCode.ImportFailed,
                () => TransferType(softwarePath, typePath, targetGroupPath, overwrite, false),
                ("softwarePath", softwarePath), ("typePath", typePath), ("targetGroupPath", targetGroupPath));
        }

        public PlcType MoveType(string softwarePath, string typePath, string targetGroupPath, bool overwrite = false)
        {
            return Operation.Run(_logger, nameof(MoveType), PortalErrorCode.ImportFailed,
                () => TransferType(softwarePath, typePath, targetGroupPath, overwrite, true),
                ("softwarePath", softwarePath), ("typePath", typePath), ("targetGroupPath", targetGroupPath));
        }

        private PlcType TransferType(string softwarePath, string typePath, string targetGroupPath, bool overwrite, bool deleteSource)
        {
            var source = GetType(softwarePath, typePath)
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"Type not found at '{typePath}'. Use 'GetTypes' to list the available types.");

            EnsureNotKnowHowProtected(source);
            EnsureConsistent(source);

            var sourceGroupPath = source.Parent is PlcTypeGroup sourceGroup
                ? GetPlcTypeGroupPath(sourceGroup, false)
                : string.Empty;

            EnsureDifferentGroup(sourceGroupPath, targetGroupPath, source.Name);

            var targetGroup = GetPlcTypeGroupByPath(softwarePath, targetGroupPath)
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"Type group not found at '{targetGroupPath}'. Use 'GetSoftwareTree' to discover valid group paths.");

            var name = source.Name;
            var directory = CreateTempExportDirectory();

            try
            {
                var file = new FileInfo(Path.Combine(directory, name + ".xml"));

                source.Export(file, ExportOptions.None);
                targetGroup.Types.Import(file, overwrite ? ImportOptions.Override : ImportOptions.None);

                if (deleteSource)
                {
                    source.Delete();
                }
            }
            finally
            {
                DeleteTempExportDirectory(directory);
            }

            return targetGroup.Types.Find(name)
                ?? throw new PortalException(PortalErrorCode.ImportFailed,
                    $"Type '{name}' was imported into '{targetGroupPath}' but could not be found afterwards.");
        }

        #endregion
    }
}
