using Siemens.Collaboration.Net;
using System.Threading.Tasks;

namespace TiaMcpServer.Siemens
{
    public static class Openness
    {
        /// <summary>
        /// TIA Portal major version whose Openness assemblies this exe was compiled against
        /// ('TiaVersion' in TiaMcpServer.csproj). V21 changed the assembly layout and identity,
        /// so an exe built for one of V20/V21 cannot attach to the other.
        /// </summary>
#if TIA_V21
        public const int BuildTiaMajorVersion = 21;
#else
        public const int BuildTiaMajorVersion = 20;
#endif

        public static int TiaMajorVersion { get; private set; }

        public static void Initialize(int? tiaMajorVersion = BuildTiaMajorVersion)
        {
            // with nuget packages:
            // 2.1 nuget package: Siemens.Collaboration.Net.TiaPortal.Openness.Resolver
            //     & User Environment Variable: TiaPortalLocation=C:\Program Files\Siemens\Automation\Portal V21
            // 2.2 nuget package: Siemens.Collaboration.Net.TiaPortal.Packages.Openness
            // 2.3 Api.Global.Openness().Initialize(tiaMajorVersion: 21); // fixed version 21

            TiaMajorVersion = tiaMajorVersion ?? BuildTiaMajorVersion;

            // Initialize the Openness API with the specified TIA Portal major version
            Api.Global.Openness().Initialize(tiaMajorVersion: tiaMajorVersion);
        }

        /// <summary>
        /// Read-only check whether the current user is a member of the 'Siemens TIA Openness' group.
        /// Unlike <see cref="IsUserInGroup"/> this never tries to add the user to the group.
        /// </summary>
        public static bool CheckUserInGroup()
        {
            return Api.Global.Openness().IsUserInGroup();
        }

        public static async Task<bool> IsUserInGroup()
        {
            if (Api.Global.Openness().IsUserInGroup())
            {
                // user is in group
                return true;
            }
            else
            {
                return await Api.Global.Openness().AddUserToGroupAsync();
            }
        }
    }
}
