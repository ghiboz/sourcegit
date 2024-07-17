﻿using System.Collections.Generic;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class TwoSideTextDiff : ObservableObject
    {
        public string File { get; set; }
        public List<Models.TextDiffLine> Old { get; set; } = new List<Models.TextDiffLine>();
        public List<Models.TextDiffLine> New { get; set; } = new List<Models.TextDiffLine>();
        public int MaxLineNumber = 0;

        public Vector SyncScrollOffset
        {
            get => _syncScrollOffset;
            set => SetProperty(ref _syncScrollOffset, value);
        }

        public Models.DiffOption Option
        {
            get;
            set;
        }

        public TwoSideTextDiff(Models.TextDiff diff, TwoSideTextDiff previous = null)
        {
            File = diff.File;
            MaxLineNumber = diff.MaxLineNumber;
            Option = diff.Option;

            foreach (var line in diff.Lines)
            {
                switch (line.Type)
                {
                    case Models.TextDiffLineType.Added:
                        New.Add(line);
                        break;
                    case Models.TextDiffLineType.Deleted:
                        Old.Add(line);
                        break;
                    default:
                        FillEmptyLines();
                        Old.Add(line);
                        New.Add(line);
                        break;
                }
            }

            FillEmptyLines();

            if (previous != null && previous.File == File)
                _syncScrollOffset = previous._syncScrollOffset;
        }

        private void FillEmptyLines()
        {
            if (Old.Count < New.Count)
            {
                int diff = New.Count - Old.Count;
                for (int i = 0; i < diff; i++)
                    Old.Add(new Models.TextDiffLine());
            }
            else if (Old.Count > New.Count)
            {
                int diff = Old.Count - New.Count;
                for (int i = 0; i < diff; i++)
                    New.Add(new Models.TextDiffLine());
            }
        }

        private Vector _syncScrollOffset = Vector.Zero;
    }
}
