﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using EnvDTE;
using EnvDTE80;

namespace SassyStudio
{
    static class InteropHelper
    {
        // Utility method from VSWebEssentials, to help out with TFS
        internal static void CheckOut(string file)
        {
            try
            {
                var dte = SassyStudioPackage.Instance.DTE;
                if (File.Exists(file) && dte.SourceControl != null && dte.Solution.FindProjectItem(file) != null)
                {
                    if (dte.SourceControl.IsItemUnderSCC(file) && !dte.SourceControl.IsItemCheckedOut(file))
                        dte.SourceControl.CheckOutItem(file);
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Failed to check out file.");
            }
        }

        internal static void AddNestedFile(DTE2 dte, string parent, string child, BuildActionType type)
        {
            // ignore if child doesn't exist
            if (!File.Exists(child)) return;
            if (dte == null) return;

            // if we can't load parent or child already part of solution, don't attempt to change anything
            ProjectItem parentItem, childItem;
            if (!TryGetProjectItem(dte.Solution, parent, out parentItem) || TryGetProjectItem(dte.Solution, child, out childItem))
                return;

            // add the child item and save project
            if (parentItem.ProjectItems == null)
                Logger.Log("ProjectItems is null. Bad news is about to happen.");

            childItem = parentItem.ProjectItems.AddFromFile(child);
            if (childItem != null)
            {
                childItem.ContainingProject.Save();
            }
            else
            {
                Logger.Log("Could not add child item to project.");
            }

            // schedule call to change build action
            // this is a work around since it seems to ignore property changes until after file saved
            // and even after that it still ignores it, so async makes it better
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => ChangeBuildActionType(dte, child, type)), DispatcherPriority.Background);
        }

        private static void ChangeBuildActionType(DTE2 dte, string path, BuildActionType type)
        {
            ProjectItem item;
            if (TryGetProjectItem(dte.Solution, path, out item))
            {
                var vsBuildAction = (int)type;
                var vsType = type.ToString();

                var actionProperty = item.Properties.Item("BuildAction");
                var typeProperty = item.Properties.Item("ItemType");

                // stop if no changes
                if (vsBuildAction.Equals(actionProperty.Value) && vsType.Equals(typeProperty.Value))
                    return;

                actionProperty.Value = vsBuildAction;
                typeProperty.Value = vsType;
                item.ContainingProject.Save();
            }
        }

        public static bool TryGetProjectItem(Solution solution, string path, out ProjectItem item)
        {
            item = null;

            if (solution != null)
                item = solution.FindProjectItem(path);

            // if we found project item, but don't have project items, attempt to find
            // item recursively
            if (item != null && item.ProjectItems == null)
                item = FindProjectItemRecursive(solution, path);

            return item != null;
        }

        private static ProjectItem FindProjectItemRecursive(Solution solution, string path)
        {
            foreach (Project project in solution.Projects)
            {
                var item = FindProjectItemRecursive(project.ProjectItems, path);
                if (item != null)
                    return item;
            }

            return null;
        }

        private static ProjectItem FindProjectItemRecursive(ProjectItems items, string path)
        {
            if (items == null)
                return null;

            foreach (ProjectItem item in items)
            {
                if (item.Properties == null || item.Properties.Count == 0) continue;

                var itemPath = item.Properties.Item("FullPath");
                if (itemPath != null && itemPath.Value != null && itemPath.Value.ToString().Equals(path, StringComparison.OrdinalIgnoreCase))
                    return item;

                var child = FindProjectItemRecursive(item.ProjectItems, path);
                if (child != null)
                    return child;
            }

            return null;
        }

        internal enum BuildActionType : int
        {
            None = 0,
            Content = 2
        }
    }
}
