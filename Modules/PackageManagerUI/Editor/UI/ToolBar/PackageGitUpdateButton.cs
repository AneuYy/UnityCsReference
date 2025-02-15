// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEditor.PackageManager.UI.Internal
{
    internal class PackageGitUpdateButton : PackageToolBarRegularButton
    {
        private PackageDatabase m_PackageDatabase;
        public PackageGitUpdateButton(PackageDatabase packageDatabase)
        {
            m_PackageDatabase = packageDatabase;
        }

        protected override bool TriggerAction(IPackageVersion version)
        {
            var installedVersion = version.package.versions.installed;
            m_PackageDatabase.Install(installedVersion.packageInfo.packageId);
            PackageManagerWindowAnalytics.SendEvent("updateGit", installedVersion.uniqueId);
            return true;
        }

        protected override bool IsVisible(IPackageVersion version) => version?.package.versions.installed?.HasTag(PackageTag.Git) == true;

        protected override string GetTooltip(IPackageVersion version, bool isInProgress)
        {
            if (isInProgress)
                return k_InProgressGenericTooltip;
            return L10n.Tr("Click to check for updates and update to latest version");
        }

        protected override string GetText(IPackageVersion version, bool isInProgress)
        {
            return L10n.Tr("Update");
        }

        protected override bool IsInProgress(IPackageVersion version) => m_PackageDatabase.IsInstallInProgress(version);
    }
}
