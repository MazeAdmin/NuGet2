﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using NuGet.Dialog.PackageManagerUI;
using NuGet.VisualStudio;

namespace NuGet.Dialog.ToolsOptionsUI {
    /// <summary>
    /// Represents the Tools - Options - Package Manager dialog
    /// </summary>
    /// <remarks>
    /// The code in this class assumes that while the dialog is open, noone is modifying the VSPackageSourceProvider directly.
    /// Otherwise, we have a problem with synchronization with the package source provider.
    /// </remarks>
    public partial class ToolsOptionsControl : UserControl {
        private IPackageSourceProvider _packageSourceProvider;
        private PackageSource _aggregateSource;
        private PackageSource _activeSource;
        private BindingSource _allPackageSources;
        private bool _initialized;

        public ToolsOptionsControl(IPackageSourceProvider packageSourceProvider) {
            _packageSourceProvider = packageSourceProvider;
            InitializeComponent();
            SetupDataBindings();
        }

        public ToolsOptionsControl()
            : this(ServiceLocator.GetInstance<IPackageSourceProvider>()) {
        }

        private void SetupDataBindings() {
            NewPackageName.TextChanged += (o, e) => UpdateUI();
            NewPackageSource.TextChanged += (o, e) => UpdateUI();
            MoveUpButton.Click += (o, e) => MoveSelectedItem(-1);
            MoveDownButton.Click += (o, e) => MoveSelectedItem(1);
            PackageSourcesListBox.SelectedIndexChanged += (o, e) => UpdateUI();
            NewPackageName.Focus();
            UpdateUI();
        }

        private void UpdateUI() {
            MoveUpButton.Enabled = PackageSourcesListBox.SelectedItem != null &&
                                    PackageSourcesListBox.SelectedIndex > 0;
            MoveDownButton.Enabled = PackageSourcesListBox.SelectedItem != null &&
                                    PackageSourcesListBox.SelectedIndex < PackageSourcesListBox.Items.Count - 1;
            removeButton.Enabled = PackageSourcesListBox.SelectedItem != null;
        }

        private void MoveSelectedItem(int offset) {
            if (PackageSourcesListBox.SelectedItem == null) {
                return;
            }

            var item = PackageSourcesListBox.SelectedItem;
            int oldIndex = PackageSourcesListBox.SelectedIndex;
            _allPackageSources.Remove(item);
            _allPackageSources.Insert(oldIndex + offset, item);

            PackageSourcesListBox.SelectedIndex = oldIndex + offset;
            UpdateUI();
        }

        internal void InitializeOnActivated() {
            if (_initialized) {
                return;
            }

            _initialized = true;
            // get packages sources
            IList<PackageSource> packageSources = _packageSourceProvider.GetPackageSources().ToList();
            _aggregateSource = packageSources.Where(ps => ps.IsAggregate).Single();
            _activeSource = _packageSourceProvider.ActivePackageSource;

            // bind to the package sources, excluding Aggregate
            _allPackageSources = new BindingSource(packageSources.Where(ps => !ps.IsAggregate).ToList(), null);
            PackageSourcesListBox.DataSource = _allPackageSources;
        }

        /// <summary>
        /// Persist the package sources, which was add/removed via the Options page, to the VS Settings store.
        /// This gets called when users click OK button.
        /// </summary>
        internal bool ApplyChangedSettings() {
            // if user presses Enter after filling in Name/Source but doesn't click Add
            // the options will be closed without adding the source, try adding before closing
            // Only apply if nothing was added
            TryAddSourceResults result = TryAddSource();
            if (result != TryAddSourceResults.NothingAdded) {
                return false;
            }

            // get package sources as ordered list
            var packageSources = PackageSourcesListBox.Items.Cast<PackageSource>().ToList();
            _packageSourceProvider.SetPackageSources(packageSources);
            // restore current active source if it still exists, or reset to aggregate source
            if (packageSources.Contains(_activeSource)) {
                _packageSourceProvider.ActivePackageSource = _activeSource;
            }
            else {
                _packageSourceProvider.ActivePackageSource = _aggregateSource;
            }
            return true;
        }

        /// <summary>
        /// This gets called when users close the Options dialog
        /// </summary>
        internal void ClearSettings() {
            // clear this flag so that we will set up the bindings again when the option page is activated next time
            _initialized = false;

            _allPackageSources = null;
            ClearNameSource();
            UpdateUI();
        }

        private void OnRemoveButtonClick(object sender, EventArgs e) {
            if (PackageSourcesListBox.SelectedItem == null) {
                return;
            }
            _allPackageSources.Remove(PackageSourcesListBox.SelectedItem);
        }

        private void OnAddButtonClick(object sender, EventArgs e) {
            TryAddSourceResults result = TryAddSource();
            if (result == TryAddSourceResults.NothingAdded) {
                MessageHelper.ShowWarningMessage(Resources.ShowWarning_NameAndSourceRequired, Resources.ShowWarning_Title);
                SelectAndFocus(NewPackageName);
            }        
        }

        private TryAddSourceResults TryAddSource() {
            var name = NewPackageName.Text.Trim();
            var source = NewPackageSource.Text.Trim();
            if (String.IsNullOrWhiteSpace(name) && String.IsNullOrWhiteSpace(source)) {
                return TryAddSourceResults.NothingAdded;
            }

            // validate name
            if (String.IsNullOrWhiteSpace(name)) {
                MessageHelper.ShowWarningMessage(Resources.ShowWarning_NameRequired, Resources.ShowWarning_Title);
                SelectAndFocus(NewPackageName);
                return TryAddSourceResults.InvalidSource;
            }

            // validate source
            if (String.IsNullOrWhiteSpace(source)) {
                MessageHelper.ShowWarningMessage(Resources.ShowWarning_SourceRequried, Resources.ShowWarning_Title);
                SelectAndFocus(NewPackageSource);
                return TryAddSourceResults.InvalidSource;
            }

            if (!(PathValidator.IsValidLocalPath(source) || PathValidator.IsValidUncPath(source) || PathValidator.IsValidUrl(source))) {
                MessageHelper.ShowWarningMessage(Resources.ShowWarning_InvalidSource, Resources.ShowWarning_Title);
                SelectAndFocus(NewPackageSource);
                return TryAddSourceResults.InvalidSource;
            }

            var sourcesList = (IEnumerable<PackageSource>)_allPackageSources.List;

            // check to see if name has already been added
            // also make sure it's not the same as the aggregate source ('All')
            bool hasName = sourcesList.Any(ps => String.Equals(name, ps.Name, StringComparison.OrdinalIgnoreCase)
                || String.Equals(name, _aggregateSource.Name, StringComparison.OrdinalIgnoreCase));
            if (hasName) {
                MessageHelper.ShowWarningMessage(Resources.ShowWarning_UniqueName, Resources.ShowWarning_Title);
                SelectAndFocus(NewPackageName);
                return TryAddSourceResults.SourceAlreadyAdded;
            }

            // check to see if source has already been added
            bool hasSource = sourcesList.Any(ps => String.Equals(source, ps.Source, StringComparison.OrdinalIgnoreCase));
            if (hasSource) {
                MessageHelper.ShowWarningMessage(Resources.ShowWarning_UniqueSource, Resources.ShowWarning_Title);
                SelectAndFocus(NewPackageSource);
                return TryAddSourceResults.SourceAlreadyAdded;
            }

            var newPackageSource = new PackageSource(source, name);
            _allPackageSources.Add(newPackageSource);
            // set selection to newly added item
            PackageSourcesListBox.SelectedIndex = PackageSourcesListBox.Items.Count - 1;

            // now clear the text boxes
            ClearNameSource();

            return TryAddSourceResults.SourceAdded;
        }

        private static void SelectAndFocus(TextBox textBox) {
            textBox.Focus();
            textBox.SelectAll();
        }

        private void ClearNameSource() {
            NewPackageName.Text = String.Empty;
            NewPackageSource.Text = String.Empty;
            NewPackageName.Focus();
        }


        private void PackageSourcesContextMenu_ItemClicked(object sender, ToolStripItemClickedEventArgs e) {
            if (e.ClickedItem == CopyPackageSourceStripMenuItem && PackageSourcesListBox.SelectedItem != null) {
                CopySelectedItem((PackageSource)PackageSourcesListBox.SelectedItem);
            }
        }

        private void PackageSourcesListBox_KeyUp(object sender, KeyEventArgs e) {
            if (e.KeyCode == Keys.C && e.Control) {
                CopySelectedItem((PackageSource)PackageSourcesListBox.SelectedItem);
            }
        }

        private static void CopySelectedItem(PackageSource selectedPackageSource) {
            Clipboard.Clear();
            Clipboard.SetText(selectedPackageSource.Source);
        }

        private void PackageSourcesListBox_MouseUp(object sender, MouseEventArgs e) {
            if (e.Button == MouseButtons.Right) {
                PackageSourcesListBox.SelectedIndex = PackageSourcesListBox.IndexFromPoint(e.Location);
            }

        }

        private readonly Color SelectionFocusGradientLightColor = Color.FromArgb(0xF9, 0xFC, 0xFF);
        private readonly Color SelectionFocusGradientDarkColor = Color.FromArgb(0xC9, 0xE0, 0xFC);
        private readonly Color SelectionFocusBorderColor = Color.FromArgb(0x89, 0xB0, 0xDF);

        private readonly Color SelectionUnfocusGradientLightColor = Color.FromArgb(0xF8, 0xF8, 0xF8);
        private readonly Color SelectionUnfocusGradientDarkColor = Color.FromArgb(0xDC, 0xDC, 0xDC);
        private readonly Color SelectionUnfocusBorderColor = Color.FromArgb(0xD9, 0xD9, 0xD9);

        private void PackageSourcesListBox_DrawItem(object sender, DrawItemEventArgs e) {
            // Draw the background of the ListBox control for each item.
            if (e.BackColor.Name == KnownColor.Highlight.ToString()) {
                using (
                    var gradientBrush = PackageSourcesListBox.Focused 
                        ? new LinearGradientBrush(e.Bounds, SelectionFocusGradientLightColor, SelectionFocusGradientDarkColor, 90.0F)
                        : new LinearGradientBrush(e.Bounds, SelectionUnfocusGradientLightColor, SelectionUnfocusGradientDarkColor, 90.0F))
                {
                    e.Graphics.FillRectangle(gradientBrush, e.Bounds);
                }
                using (var borderPen = PackageSourcesListBox.Focused 
                    ? new Pen(SelectionFocusBorderColor)
                    : new Pen(SelectionUnfocusBorderColor)) {
                    e.Graphics.DrawRectangle(borderPen, e.Bounds.Left, e.Bounds.Top, e.Bounds.Width - 1, e.Bounds.Height - 1);
                }
            
            }
            else {
                // alternate background color for even/odd rows
                Color backColor = e.Index % 2 == 0
                                      ? Color.FromKnownColor(KnownColor.Window)
                                      : Color.FromArgb(0xF6, 0xF6, 0xF6);
                using (Brush backBrush = new SolidBrush(backColor)) {
                    e.Graphics.FillRectangle(backBrush, e.Bounds);
                }
            }

            if (e.Index < 0 || e.Index >= PackageSourcesListBox.Items.Count) {
                return;
            }

            PackageSource currentItem = (PackageSource)PackageSourcesListBox.Items[e.Index];

            using (StringFormat drawFormat = new StringFormat())
            using (Brush foreBrush = new SolidBrush(Color.FromKnownColor(KnownColor.WindowText)))
            using (Font italicFont = new Font(e.Font, FontStyle.Italic))
            {
                drawFormat.Alignment = StringAlignment.Near;
                drawFormat.Trimming = StringTrimming.EllipsisCharacter;
                drawFormat.LineAlignment = StringAlignment.Near;
                // draw package source as
                // 1. Name
                //    Source (italics)
                int ordinal = e.Index + 1;
                e.Graphics.DrawString(ordinal + ".", e.Font, foreBrush, e.Bounds,
                                        drawFormat);
                var nameBounds = NewBounds(e.Bounds, 16, 0);
                e.Graphics.DrawString(currentItem.Name, e.Font, foreBrush, nameBounds, drawFormat);
                var sourceBounds = NewBounds(nameBounds, 0, 16);
                e.Graphics.DrawString(currentItem.Source, italicFont, foreBrush, sourceBounds, drawFormat);

                // If the ListBox has focus, draw a focus rectangle around the selected item.
                e.DrawFocusRectangle();
            }
        }

        private static Rectangle NewBounds(Rectangle sourceBounds, int xOffset, int yOffset) {
            return new Rectangle(sourceBounds.Left + xOffset, sourceBounds.Top + yOffset,
                sourceBounds.Width - xOffset, sourceBounds.Height - yOffset);
        }
    }

    internal enum TryAddSourceResults {
        NothingAdded = 0,
        SourceAdded = 1,
        InvalidSource = 2,
        SourceAlreadyAdded = 3
    }
}
