using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        [WriteTool]
        [McpServerTool(Name = "CopyHmiScreen", Title = "Copy HMI screen", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Copy a WinCC Unified screen to a new screen in the same folder, item by item: every writable property (position, size, colors, fonts, ...), texts, tag and script dynamizations, event scripts and faceplate interfaces. 'replace' rewrites text in every copied string (tag names, texts, scripts, interface values), e.g. 'Servo01=>Servo02' to make the screen of the next axis. Openness has no screen copy of its own, so anything it cannot read is listed under 'skipped'. A refused write aborts the whole copy")]
        public static ResponseHmiScreenCopy CopyHmiScreen(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: the screen to copy, as 'GetHmiScreens' lists it")] string screenName,
            [Description("newScreenName: name of the new screen")] string newScreenName,
            [Description("replace: optional text replacements applied in order to every copied string, as 'from=>to' pairs separated by ';', e.g. 'Servo01=>Servo02;SM118=>SM119'. Matching is case-sensitive")] string replace = "",
            [Description("screenNumber: optional screen number of the new screen")] ushort? screenNumber = null)
        {
            var replacements = ParseReplacements(replace);

            // Read outside the write transaction: a failed read is skipped and reported there, while
            // any exception inside the transaction would make TIA Portal refuse the commit.
            HmiScreenSnapshot snapshot;

            try
            {
                snapshot = Portal.ReadHmiScreenSnapshot(softwarePath, screenName);
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }

            return Guarded(nameof(CopyHmiScreen), () =>
            {
                var result = Portal.WriteHmiScreenCopy(softwarePath, snapshot, newScreenName, replacements, screenNumber);

                return new ResponseHmiScreenCopy
                {
                    Message = $"Screen '{screenName}' copied to '{newScreenName}': {result.Items} item(s), {result.Attributes} propert(ies), " +
                              $"{result.Texts} text(s), {result.Dynamizations} dynamization(s), {result.Events} event script(s); " +
                              $"{result.Replaced} string(s) changed by 'replace'." +
                              (snapshot.Skipped.Count + result.Warnings.Count > 0
                                  ? $" WARNING: {snapshot.Skipped.Count + result.Warnings.Count} thing(s) not copied - see 'skipped' and 'warnings'."
                                  : string.Empty) +
                              $" {SaveHint}",
                    ScreenName = result.Screen?.Name,
                    ScreenNumber = result.Screen?.ScreenNumber,
                    Items = result.Items,
                    Properties = result.Attributes,
                    Texts = result.Texts,
                    Dynamizations = result.Dynamizations,
                    Events = result.Events,
                    Replaced = result.Replaced,
                    Skipped = snapshot.Skipped.Count > 0 ? snapshot.Skipped : null,
                    Warnings = result.Warnings.Count > 0 ? result.Warnings : null,
                    Meta = OkMeta()
                };
            });
        }

        private static List<KeyValuePair<string, string>> ParseReplacements(string replace)
        {
            var pairs = new List<KeyValuePair<string, string>>();

            foreach (var entry in (replace ?? string.Empty).Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var text = entry.Trim('\r', ' ');

                if (text.Length == 0)
                {
                    continue;
                }

                var arrow = text.IndexOf("=>", StringComparison.Ordinal);

                if (arrow <= 0)
                {
                    throw new McpException($"'replace' entry '{text}' is not 'from=>to'. Example: 'Servo01=>Servo02;SM118=>SM119'.");
                }

                pairs.Add(new KeyValuePair<string, string>(text.Substring(0, arrow), text.Substring(arrow + 2)));
            }

            return pairs;
        }
    }
}
