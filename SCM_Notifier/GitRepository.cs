﻿//
// SCM Notifier
// Copyright Sung-Ho Lee
//
// This file is part of SCM Notifier.
//
// SCM Notifier is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 3 of the License, or
// (at your option) any later version.
//
// SCM Notifier is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>
//

using System;
using System.Collections;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace pocorall.SCM_Notifier
{
    public class GitRepository : ScmRepository
    {
        private static readonly Regex regexUpToDate;

        static GitRepository()
        {
            regexUpToDate = new Regex(@"^\s=\s\[up to date\]\s.+?\s->\s.+$",RegexOptions.Compiled); 
        }

        public GitRepository(string path) : base("Git", path, ScmRepository.PathType.Directory)
        {
            
        }

        public override int GetRepositoryHeadRevision()
        {
            return GetRepositoryRevision(Config.GitPath, Path, "HEAD");
        }

        public override int GetRepositoryCommitedRevision()
        {
            return GetRepositoryRevision(Config.GitPath, Path, "COMMITTED");
        }

        public override void OpenChangeLogWindow(bool updateRevisions)
        {
            this.OpenLogWindow();
            return;
            if (updateRevisions)
            {
                reviewedRevision = GetRepositoryHeadRevision();
                updateRevision = GetRepositoryCommitedRevision();
            }
            string arguments = String.Format("/command:log /path:\"{0}\" /revend:{1}", Path, updateRevision);
            ExecuteProcess(Config.TortoiseGitPath, null, arguments, false, false);
        }

        public override void OpenLogWindow()
        {
            string arguments = String.Format("/command:log /path:\"{0}\"", Path);
            ExecuteProcess(Config.TortoiseGitPath, null, arguments, false, false);
        }

        /// <summary>
        /// This method waits until updating will finish
        /// </summary>
        public override void Update(bool updateAll)
        {
            string arguments = String.Format("/command:pull /path:\"{0}\" /notempfile", Path);
            ExecuteResult er = ExecuteProcess(Config.TortoiseGitPath, Path, arguments, true, false);
            string d = ( er.processOutput);
        }

        private bool isModified(string response)
        {
            return !(response.Contains("othing added to commit") || response.Contains("othing to commit"));
        }

        public override void Commit()
        {
            string arguments = String.Format("status -u \"{0}\"", Path);
            ExecuteResult er = ExecuteProcess(Config.GitPath, Path, arguments, true, true);
            if (!isModified(er.processOutput))
            {
                arguments = String.Format("/command:push /path:\"{0}\" /notempfile", Path);
                er = ExecuteProcess(Config.TortoiseGitPath, null, arguments, false, false);
                svnFolderProcesses.Add(new ScmRepositoryProcess(this, er.process, false));
            }
            else
            {
                arguments = String.Format("/command:commit /path:\"{0}\" /notempfile", Path);
                er = ExecuteProcess(Config.TortoiseGitPath, null, arguments, false, false);
                svnFolderProcesses.Add(new ScmRepositoryProcess(this, er.process, false));
            }
        }

        public override ScmRepositoryStatus GetStatus()
        {
            string path = Path;
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                OnErrorAdded(path, "File or folder don't exist!");
                return ScmRepositoryStatus.Error;
            }

            try
            {
                ExecuteResult er = ExecuteProcess(Config.GitPath, path,"fetch --all --dry-run -v", true, true);
                if (er.processError.Contains("Could not fetch"))
                {
                    return ScmRepositoryStatus.Error;
                }

                bool needUpdate = this.IsNeedUpdate(er.processError);

                string arguments = String.Format("status -u \"{0}\"", path);
                er = ExecuteProcess(Config.GitPath, path, arguments, true, true);

                if (er.processOutput.Contains("have diverged"))
                {
                    return ScmRepositoryStatus.NeedUpdate_Modified;
                }

                if (er.processOutput.Contains("branch is ahead of"))
                {
                    return needUpdate ? ScmRepositoryStatus.NeedUpdate_Modified : ScmRepositoryStatus.UpToDate_Modified;
                }
                if (er.processOutput.Contains("Changed but not updated") || er.processOutput.Contains("Changes not staged for commit"))
                {
                    return needUpdate? ScmRepositoryStatus.NeedUpdate_Modified: ScmRepositoryStatus.UpToDate_Modified;
                }
                if (!isModified(er.processOutput))
                {
                    return needUpdate? ScmRepositoryStatus.NeedUpdate : ScmRepositoryStatus.UpToDate;
                }

                return ScmRepositoryStatus.Unknown;
            }
            catch(Exception e)
            {
                OnErrorAdded(path, e.Message);
                return ScmRepositoryStatus.Error;
            }
        }

        private bool IsNeedUpdate(string str)
        {
            using (var sr = new StringReader(str))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {  
                    if (line.StartsWith("From")) continue;
                    if (!regexUpToDate.IsMatch(line)) return true;
                }
            }
            return false;
        }

        public override void BeginUpdateSilently()
        {
            // Skip this folder if update or commit is in progress
            foreach (ScmRepositoryProcess sp in svnFolderProcesses)
                if (sp.repository.Path == Path)
                    return;

            updateRevision = GetRepositoryCommitedRevision();
            string arguments = String.Format("update --non-interactive \"{0}\"", Path);
            ExecuteResult er = ExecuteProcess(Config.GitPath, null, arguments, false, false);
            Config.WriteLog("Svn", arguments);
            svnFolderProcesses.Add(new ScmRepositoryProcess(this, er.process, true));
        }
    }
}