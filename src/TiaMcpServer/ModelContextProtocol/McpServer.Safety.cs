using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        // Read-only safety program status; see Portal.Safety.cs. No login, no password, no change.

        [McpServerTool(Name = "GetSafetyInfo", Title = "Get safety program info", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Read the safety program status of an F-CPU without changing anything: whether a safety password is set and logged on, the safety settings and runtime groups (F-OB, main safety block, cycle times), and the offline signature of every F-block. 'signatureHash' combines all F-block signatures, so comparing it with an earlier call shows whether any F-block changed (it is not TIA's F-collective signature). A signature of 16#00000000 means the block has no valid signature, e.g. changed since the last compile")]
        public static ResponseSafetyInfo GetSafetyInfo(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("includeSignatures: read the F-block signatures (default true); false returns only the status, settings and runtime groups")] bool includeSignatures = true,
            [Description("groupPath: optional block group to look in, e.g. '005_Safety'; empty checks every block (slower on a large PLC)")] string groupPath = "",
            [Description("regexName: optional regular expression on the block name")] string regexName = "")
        {
            try
            {
                var info = Portal.GetSafetyInfo(softwarePath, includeSignatures, groupPath, regexName, TimeSpan.FromSeconds(40));

                if (!info.IsFailsafeCpu)
                {
                    return new ResponseSafetyInfo
                    {
                        Message = $"'{softwarePath}' is not an F-CPU (no safety administration), so it has no safety program.",
                        IsFailsafeCpu = false,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                var blocks = info.Blocks.Select(b => new ResponseSafetyBlockSignature { Path = b.Path, Signature = b.Signature, Valid = b.Valid }).ToList();
                var invalid = blocks.Count(b => !b.Valid);

                var message = $"Safety program of '{softwarePath}': password {(info.PasswordSet == true ? "set" : info.PasswordSet == false ? "not set" : "unknown")}, " +
                              $"{(info.LoggedOn == true ? "logged on" : "not logged on")}, {info.RuntimeGroups.Count} runtime group(s)";

                if (includeSignatures)
                {
                    message += $"; {blocks.Count} F-block(s) among {info.BlocksChecked} block(s) checked, {invalid} without a valid signature";
                    message += info.StoppedReason != null
                        ? $" - INCOMPLETE: {info.StoppedReason}"
                        : info.SignatureHash != null ? $", signature hash {info.SignatureHash}" : string.Empty;
                }

                return new ResponseSafetyInfo
                {
                    Message = message + ".",
                    IsFailsafeCpu = true,
                    PasswordSet = info.PasswordSet,
                    LoggedOn = info.LoggedOn,
                    Settings = info.Settings,
                    RuntimeGroups = info.RuntimeGroups.Select(g => new ResponseSafetyRuntimeGroup
                    {
                        Name = g.Name,
                        FobName = g.FobName,
                        FobEventClass = g.FobEventClass,
                        MainSafetyBlockName = g.MainSafetyBlockName,
                        MainSafetyBlockIDbName = g.MainSafetyBlockIDbName,
                        InfoDbName = g.InfoDbName,
                        MaximumCycleTime = g.MaximumCycleTime,
                        WarnCycleTime = g.WarnCycleTime
                    }).ToList(),
                    Blocks = includeSignatures ? blocks : null,
                    BlocksChecked = info.BlocksChecked,
                    FBlockCount = blocks.Count,
                    InvalidCount = invalid,
                    SignatureHash = info.SignatureHash,
                    StoppedReason = info.StoppedReason,
                    Warnings = info.Warnings.Count > 0 ? info.Warnings : null,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["complete"] = info.StoppedReason == null
                    }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error reading the safety program of '{softwarePath}': {ex.Message}", ex);
            }
        }
    }
}
