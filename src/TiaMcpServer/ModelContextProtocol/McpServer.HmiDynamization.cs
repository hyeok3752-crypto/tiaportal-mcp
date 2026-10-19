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
        // WinCC Unified screen item dynamizations in full (BACKLOG C6). See Portal.HmiDynamization.cs.

        #region read

        [McpServerTool(Name = "GetHmiDynamizations", Title = "Get HMI dynamizations", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use to find out what drives a property of a WinCC Unified screen item, e.g. which tag value or bit turns a lamp green. Lists each dynamization in full: the tag with its mapping table (value, range or bit -> property value, colors as '#RRGGBB') or formula, script code with its trigger, flashing colors and rate, resource list. Only items that have a dynamization are listed")]
        public static ResponseHmiDynamizations GetHmiDynamizations(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen, as 'GetHmiScreens' lists it, e.g. '6550 Safety Door'")] string screenName,
            [Description("regexName: optional name or regular expression to filter the items, e.g. 'Button_1$'. Empty (default) reads all items")] string regexName = "",
            [Description("propertyName: optional property to show, e.g. 'BackColor'. Empty (default) shows every dynamized property")] string propertyName = "")
        {
            try
            {
                var result = Portal.GetHmiDynamizations(softwarePath, screenName, regexName, propertyName, TimeSpan.FromSeconds(40));
                var count = result.Items.Sum(i => i.Dynamizations.Count);

                return new ResponseHmiDynamizations
                {
                    Message = $"{count} dynamization(s) on {result.Items.Count} of {result.ItemsLookedAt} item(s) of screen '{screenName}' in '{softwarePath}'." +
                              (result.StoppedReason != null ? $" Incomplete: {result.StoppedReason}." : string.Empty),
                    Items = result.Items,
                    StoppedReason = result.StoppedReason,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["count"] = count,
                        ["complete"] = result.StoppedReason == null
                    }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error reading the dynamizations of screen '{screenName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        #endregion
    }
}
