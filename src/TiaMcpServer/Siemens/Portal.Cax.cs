using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.Cax;
using System;
using System.IO;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        #region CAx export/import (AutomationML, for ECAD/EPLAN round-trip)

        // Siemens.Engineering.Cax.CaxProvider - a service on ProjectBase/Device, same GetService<T>()
        // pattern used elsewhere in this file. Export writes an AML file describing the HW config
        // (devices, modules, IO addresses) for an ECAD tool like EPLAN; Import brings a (possibly
        // hand-edited, e.g. with real cable/terminal data added) AML file back in. devicePath
        // empty/null exports the whole project.
        public TransferResult ExportCax(string? devicePath, string exportPath)
        {
            return Operation.Run(_logger, nameof(ExportCax), PortalErrorCode.ExportFailed,
                () =>
                {
                    if (IsProjectNull())
                    {
                        throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
                    }

                    Operation.RequireAbsolutePath(exportPath);

                    var caxProvider = _project!.GetService<CaxProvider>()
                        ?? throw new PortalException(PortalErrorCode.InvalidState, "CAx export is not available for this project/session");

                    TransferResult result;
                    if (string.IsNullOrEmpty(devicePath))
                    {
                        var fileName = Path.Combine(exportPath, $"{_project!.Name}.aml");
                        result = caxProvider.Export(_project!, new FileInfo(fileName));
                    }
                    else
                    {
                        var device = GetDevice(devicePath)
                            ?? throw new PortalException(PortalErrorCode.NotFound, $"Device not found at '{devicePath}'");
                        var fileName = Path.Combine(exportPath, $"{FileNames.Safe(device.Name)}.aml");
                        result = caxProvider.Export(device, new FileInfo(fileName));
                    }

                    return result;
                },
                ("devicePath", devicePath ?? ""), ("exportPath", exportPath));
        }

        // Merge behavior on conflict (mergeOption) defaults to MoveToParkingLot - the only one of
        // the three CaxImportOptions values that doesn't silently overwrite or silently discard
        // AML content on a clash; conflicting items land in TIA's own Parking Lot for a human to
        // resolve. Openness exposes no preview/dry-run for CAx import, so this needs an explicit
        // confirm=true.
        // Uses the logFilePath-accepting overload (returns bool, not TransferResult) rather than
        // Import(FileInfo, CaxImportOptions): live testing showed TransferResult.Messages only
        // carries Warning/Information entries plus a final summary line - none of the individual
        // Error-level messages show up there, only the aggregate ErrorCount. The log file is
        // where TIA actually writes per-item error detail.
        public (bool Success, string LogText, string LogPath) ImportCax(string importPath, CaxImportOptions mergeOption = CaxImportOptions.MoveToParkingLot, bool confirm = false)
        {
            return Operation.Run(_logger, nameof(ImportCax), PortalErrorCode.ExportFailed,
                () =>
                {
                    if (IsProjectNull())
                    {
                        throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
                    }

                    var fileInfo = new FileInfo(importPath);
                    if (!fileInfo.Exists)
                    {
                        throw new PortalException(PortalErrorCode.NotFound, $"Import file not found at '{importPath}'");
                    }

                    if (!confirm)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            "CAx import can restructure hardware config; call again with confirm=true to actually import. " +
                            "Openness has no preview for this operation, so double-check the AML source first.");
                    }

                    var logPath = Path.Combine(Path.GetTempPath(), "TiaMcpServer", "cax-import-logs",
                        $"{Path.GetFileNameWithoutExtension(importPath)}_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

                    var caxProvider = _project!.GetService<CaxProvider>()
                        ?? throw new PortalException(PortalErrorCode.InvalidState, "CAx import is not available for this project/session");

                    var success = caxProvider.Import(fileInfo, new FileInfo(logPath), mergeOption);
                    var logText = File.Exists(logPath) ? File.ReadAllText(logPath) : "(TIA did not write a log file)";

                    return (success, logText, logPath);
                },
                ("importPath", importPath), ("mergeOption", mergeOption.ToString()));
        }

        #endregion
    }
}
