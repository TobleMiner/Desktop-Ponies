﻿Imports System.Globalization
Imports System.IO
Imports DesktopSprites.SpriteManagement

''' <summary>
''' This is the form that handles startup and pony selection.
''' </summary>
Public Class MainForm
#Region "Fields and Properties"
    Private initialized As Boolean
    Private loading As Boolean
    Private ReadOnly loadWatch As New Diagnostics.Stopwatch()
    Private ReadOnly worker As New IdleWorker(Me)

    Private oldWindowState As FormWindowState
    Private layoutPendingFromRestore As Boolean

    Private autoStarted As Boolean

    Private animator As DesktopPonyAnimator
    Private ponyViewer As ISpriteCollectionView
    Private ReadOnly startupPonies As New List(Of Pony)()
    Private ponies As PonyCollection
    Private screensaverForms As List(Of ScreensaverBackgroundForm)

    Private preventLoadProfile As Boolean

    Private notTaggedFilterIndex As Integer
    Private ReadOnly selectionControlFilter As New Dictionary(Of PonySelectionControl, Boolean)()
    Private ponyOffset As Integer
    Private ReadOnly selectionControlsFilteredVisible As IEnumerable(Of PonySelectionControl)
#End Region

#Region "Initialization"
    Public Sub New()
        InitializeComponent()
        selectionControlsFilteredVisible =
            PonySelectionPanel.Controls.Cast(Of PonySelectionControl).Where(Function(control) selectionControlFilter(control))
        Icon = My.Resources.Twilight
        Text = "Desktop Ponies v" & General.GetAssemblyVersion().ToDisplayString()
        initialized = True
        EvilGlobals.Main = Me
    End Sub

    Private Sub Main_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        BeginInvoke(New MethodInvoker(AddressOf LoadInternal))
    End Sub

    ''' <summary>
    ''' Read all configuration files and pony folders.
    ''' </summary>
    Private Sub LoadInternal()
        loadWatch.Restart()
        UseWaitCursor = True
        loading = True

        PonyPaginationPanel.Enabled = False
        PonySelectionPanel.Enabled = False
        SelectionControlsPanel.Enabled = False

        Update()

        If ProcessCommandLine() Then Return

        ' Load the profile that was last in use by this user.
        Dim profile = Options.DefaultProfileName
        Dim profileFile As StreamReader = Nothing
        Try
            profileFile = New StreamReader(Path.Combine(Options.ProfileDirectory, "current.txt"), System.Text.Encoding.UTF8)
            profile = profileFile.ReadLine()
        Catch ex As FileNotFoundException
            ' We don't mind if no preferred profile is saved.
        Catch ex As DirectoryNotFoundException
            ' In screensaver mode, the user might set a bad path. We'll ignore it for now.
        Finally
            If profileFile IsNot Nothing Then profileFile.Close()
        End Try
        GetProfiles(profile)

        Dim startedAsScr = Environment.GetCommandLineArgs()(0).EndsWith(".scr", StringComparison.OrdinalIgnoreCase)
        If startedAsScr Then
            Dim screensaverPath = EvilGlobals.TryGetScreensaverPath()
            If screensaverPath Is Nothing Then
                MessageBox.Show(
                    Me, "The screensaver has not yet been configured, or the previous configuration is invalid. Please reconfigure now.",
                    "Configuration Missing", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Dim result = EvilGlobals.SetScreensaverPath()
                If Not result Then
                    MessageBox.Show(Me, "You will be unable to run Desktop Ponies as a screensaver until it is configured.",
                                    "Configuration Aborted", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Else
                    MessageBox.Show(Me, "Restart Desktop Ponies for the new settings to take effect.",
                                    "Configuration Successful", MessageBoxButtons.OK, MessageBoxIcon.Information)
                End If
                Close()
                Return
            End If
            ' Start in a minimized state to load, and attempt to open the screensaver profile.
            ShowInTaskbar = False
            WindowState = FormWindowState.Minimized
            Try
                Options.LoadProfile("screensaver", False)
            Catch
                Options.LoadDefaultProfile()
            End Try
        End If

        Threading.ThreadPool.QueueUserWorkItem(Sub() LoadTemplates())
        Threading.ThreadPool.QueueUserWorkItem(Sub() CheckForNewVersion())
    End Sub

    Private Function ProcessCommandLine() As Boolean
        Try
            Dim args = Environment.GetCommandLineArgs()

            If args.Length = 1 AndAlso args(0).EndsWith(".scr", StringComparison.OrdinalIgnoreCase) Then
                'for some versions of windows, starting with no parameters is the same as /c (configure)
                EvilGlobals.SetScreensaverPath()
                Me.Close()
                Return True
            End If

            ' Process command line arguments (used for the screensaver mode).
            If My.Application.CommandLineArgs.Count >= 1 Then
                Select Case Split(args(1).Trim(), ":")(0).ToLowerInvariant()
                    Case "autostart"
                        autoStarted = True
                        ShowInTaskbar = False

                        Try
                            Options.LoadProfile("autostart", False)
                        Catch
                            Options.LoadDefaultProfile()
                        End Try

                        'windows is telling us "start as a screensaver"
                    Case "/s"
                        Dim path = EvilGlobals.TryGetScreensaverPath()
                        If path Is Nothing Then
                            MessageBox.Show(Me, "The screensaver path has not been configured correctly." &
                                            " Until it has been set, the screensaver mode cannot be used.",
                                            "Screensaver Not Configured", MessageBoxButtons.OK, MessageBoxIcon.Error)
                            Close()
                            Return True
                        End If

                        EvilGlobals.InstallLocation = path
                        EvilGlobals.InScreensaverMode = True
                        autoStarted = True
                        ShowInTaskbar = False
                        WindowState = FormWindowState.Minimized

                        Try
                            Options.LoadProfile("screensaver", False)
                        Catch
                            Options.LoadDefaultProfile()
                        End Try

                        'windows says: "preview screensaver".  This isn't implemented so just quit
                    Case "/p"
                        Me.Close()
                        Return True
                        'windows says:  "configure screensaver"
                    Case "/c"
                        EvilGlobals.SetScreensaverPath()
                        Me.Close()
                        Return True
                    Case Else
                        MessageBox.Show(
                            Me,
                            "Invalid command line argument. Usage: " & ControlChars.NewLine &
                            "desktop ponies.exe autostart - " &
                            "Automatically start with saved settings (or defaults if no settings are saved)" & ControlChars.NewLine &
                            "desktop ponies.exe /s - " &
                            "Start in screensaver mode (you need to run /c first to configure the path to the pony files)" &
                            ControlChars.NewLine &
                            "desktop ponies.exe /c - " &
                            "Configure the path to pony files, only used for Screensaver mode." & ControlChars.NewLine &
                            "desktop ponies.exe /p - " &
                            "Screensaver preview use only.Not implemented.",
                            "Invalid Arguments", MessageBoxButtons.OK, MessageBoxIcon.Error)
                        Me.Close()
                        Return True
                End Select
            End If
        Catch ex As Exception
            Program.NotifyUserOfNonFatalException(ex, "Error processing command line arguments. They will be ignored.")
        End Try
        Return False
    End Function

    Private Sub LoadTemplates()
        ' Load ponies.
        ponies = New PonyCollection(
            True,
            Sub(count)
                worker.QueueTask(Sub() LoadingProgressBar.Maximum = count)
            End Sub,
            Sub(pony)
                worker.QueueTask(Sub()
                                     AddToMenu(pony)
                                     LoadingProgressBar.Value += 1
                                 End Sub)
            End Sub)

        ' Sort controls by name.
        worker.QueueTask(Sub()
                             Dim selectionControls = PonySelectionPanel.Controls.Cast(Of PonySelectionControl)().ToArray()
                             Array.Sort(selectionControls,
                                        Function(a, b) StringComparer.OrdinalIgnoreCase.Compare(
                                            a.PonyBase.Directory, b.PonyBase.Directory))
                             PonySelectionPanel.SuspendLayout()
                             For i = 0 To selectionControls.Length - 1
                                 PonySelectionPanel.Controls.SetChildIndex(selectionControls(i), i)
                             Next
                             PonySelectionPanel.ResumeLayout()
                         End Sub)

        ' Wait for ponies and houses to load.
        worker.WaitOnAllTasks()
        If Not ponies.Bases.Any() Then
            SmartInvoke(Sub()
                            MessageBox.Show(Me, "Sorry, but you don't seem to have any usable ponies installed. " &
                                            "There should have at least been a 'Derpy' folder in the same spot as this program.",
                                            "No Ponies Found", MessageBoxButtons.OK, MessageBoxIcon.Information)
                            GoButton.Enabled = False
                        End Sub)
        End If

        ' Load pony counts.
        worker.QueueTask(AddressOf Options.LoadPonyCounts)

        ' Show images in unison (although images still loading will appear as they become available).
        worker.QueueTask(Sub()
                             For Each control As PonySelectionControl In PonySelectionPanel.Controls
                                 control.ShowPonyImage = True
                             Next
                         End Sub)

        ' Finish loading.
        worker.QueueTask(Sub()
                             Console.WriteLine("Templates Loaded in {0:0.00s}", loadWatch.Elapsed.TotalSeconds)

                             PonyPaginationLabel.Text = String.Format(
                                     CultureInfo.CurrentCulture, "Viewing {0} ponies", PonySelectionPanel.Controls.Count)

                             If Not Runtime.IsMono Then LoadingProgressBar.Visible = False
                             LoadingProgressBar.Value = 0
                             LoadingProgressBar.Maximum = 1

                             If autoStarted Then
                                 LoadPonies()
                             Else
                                 CountSelectedPonies()

                                 PoniesPerPage.Maximum = PonySelectionPanel.Controls.Count
                                 PaginationEnabled.Enabled = True
                                 PaginationEnabled.Checked = Not OperatingSystemInfo.IsWindows

                                 PonySelectionPanel.Enabled = True
                                 SelectionControlsPanel.Enabled = True
                                 AnimationTimer.Enabled = True
                             End If

                             General.FullCollect()
                             loading = False
                             UseWaitCursor = False

                             loadWatch.Stop()
                             Console.WriteLine("Loaded in {0:0.00s} ({1} templates)",
                                               loadWatch.Elapsed.TotalSeconds, PonySelectionPanel.Controls.Count)
                         End Sub)
    End Sub

    Private Sub AddToMenu(ponyBase As PonyBase)
        Dim ponySelection As New PonySelectionControl(ponyBase, ponyBase.Behaviors(0).RightImage.Path, False)
        AddHandler ponySelection.PonyCount.TextChanged, AddressOf HandleCountChange
        If ponyBase.Directory = ponyBase.RandomDirectory Then
            ponySelection.NoDuplicates.Visible = True
            ponySelection.NoDuplicates.Checked = Options.NoRandomDuplicates
            AddHandler ponySelection.NoDuplicates.CheckedChanged, Sub() Options.NoRandomDuplicates = ponySelection.NoDuplicates.Checked
        End If

        selectionControlFilter.Add(ponySelection, True)
        PonySelectionPanel.Controls.Add(ponySelection)
    End Sub

    Private Sub HandleCountChange(sender As Object, e As EventArgs)
        CountSelectedPonies()
    End Sub

    Private Sub CountSelectedPonies()
        Dim totalPonies = PonySelectionPanel.Controls.Cast(Of PonySelectionControl).Sum(Function(psc) psc.Count)
        PonyCountValueLabel.Text = totalPonies.ToString(CultureInfo.CurrentCulture)
    End Sub

    Private Sub CheckForNewVersion()
        Dim info = CommunityDialog.CommunityInfo.Retrieve()
        If info IsNot Nothing Then
            worker.QueueTask(Sub()
                                 CommunityLink.Visible = True
                                 AddHandler CommunityLink.LinkClicked,
                                     Sub()
                                         Using dialog = New CommunityDialog(info)
                                             dialog.ShowDialog(Me)
                                         End Using
                                     End Sub
                                 If info.NewerVersionAvailable Then CommunityLink.Text &= " [New Version Available!]"
                             End Sub)
        End If
    End Sub
#End Region

#Region "Selection"
    Private Sub ZeroPoniesButton_Click(sender As Object, e As EventArgs) Handles ZeroPoniesButton.Click
        For Each ponyPanel As PonySelectionControl In PonySelectionPanel.Controls
            ponyPanel.Count = 0
        Next
    End Sub

    Private Sub SaveProfileButton_Click(sender As Object, e As EventArgs) Handles SaveProfileButton.Click
        Dim profileToSave = ProfileComboBox.Text

        If profileToSave = "" Then
            MessageBox.Show(Me, "Enter a profile name first!", "No Profile Name", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Exit Sub
        End If

        If profileToSave = Options.DefaultProfileName Then
            MessageBox.Show(
                Me, "Cannot save over the '" & Options.DefaultProfileName & "' profile. " &
                "To create a new profile, type a new name for the profile into the box. You will then be able to save the profile.",
                "Invalid Profile Name", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Exit Sub
        End If

        Options.SaveProfile(profileToSave)

        If Not ProfileComboBox.Items.Contains(profileToSave) Then
            ProfileComboBox.Items.Add(profileToSave)
        End If
        ProfileComboBox.SelectedItem = profileToSave

        MessageBox.Show(Me, "Profile '" & profileToSave & "' saved.", "Profile Saved",
                        MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    Private Sub LoadProfileButton_Click(sender As Object, e As EventArgs) Handles LoadProfileButton.Click
        Options.LoadProfile(ProfileComboBox.Text, True)
        ReloadFilterCategories()
    End Sub

    Private Sub OnePoniesButton_Click(sender As Object, e As EventArgs) Handles OnePoniesButton.Click
        For Each ponyPanel As PonySelectionControl In PonySelectionPanel.Controls
            ponyPanel.Count = 1
        Next
    End Sub

    Private Sub OptionsButton_Click(sender As Object, e As EventArgs) Handles OptionsButton.Click
        Using form = New OptionsForm()
            form.ShowDialog(Me)
            ReloadFilterCategories()
        End Using
    End Sub

    Private Sub PonyEditorButton_Click(sender As Object, e As EventArgs) Handles PonyEditorButton.Click

        EvilGlobals.InPreviewMode = True
        Me.Visible = False
        Using form = New PonyEditor()
            form.ShowDialog(Me)

            PonyShutdown()

            EvilGlobals.InPreviewMode = False
            If Not Me.IsDisposed Then
                Me.Visible = True
            End If

            If form.ChangesMade Then
                ResetPonySelection()
                FilterAllRadio.Checked = True
                LoadingProgressBar.Visible = True
                '(We need to reload everything to account for anything changed while in the editor)
                LoadInternal()
            End If
        End Using

    End Sub

    Private Sub GamesButton_Click(sender As Object, e As EventArgs) Handles GamesButton.Click
        Try
            Me.Visible = False
            Using gameForm As New GameSelectionForm(ponies)
                If gameForm.ShowDialog(Me) = DialogResult.OK Then
                    startupPonies.Clear()
                    PonyStartup()
                    EvilGlobals.CurrentGame.Setup()
                    animator.Start()
                Else
                    If Me.IsDisposed = False Then
                        Me.Visible = True
                    End If
                End If
            End Using
        Catch ex As Exception
            Program.NotifyUserOfNonFatalException(ex, "Error loading games.")
#If DEBUG Then
            Throw
#End If
        End Try
    End Sub

    Private Sub GetProfiles(profileToAttemptToLoad As String)
        ProfileComboBox.Items.Clear()
        ProfileComboBox.Items.Add(Options.DefaultProfileName)
        Dim profiles = Options.GetKnownProfiles()
        If profiles IsNot Nothing Then ProfileComboBox.Items.AddRange(profiles)
        Dim profileIndex = ProfileComboBox.Items.IndexOf(profileToAttemptToLoad)
        If profileIndex <> -1 Then
            ProfileComboBox.SelectedIndex = profileIndex
        Else
            ProfileComboBox.SelectedIndex = 0 ' Default profile.
        End If
    End Sub

    Private Sub CopyProfileButton_Click(sender As Object, e As EventArgs) Handles CopyProfileButton.Click
        preventLoadProfile = True

        Dim copiedProfileName = InputBox("Enter name of new profile to copy to:")
        copiedProfileName = Trim(copiedProfileName)
        If copiedProfileName = "" Then
            MessageBox.Show(Me, "Can't copy to a profile with a blank name! Please choose another name.", "Invalid Profile Name",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Exit Sub
        End If

        If copiedProfileName = Options.DefaultProfileName Then
            MessageBox.Show(Me, "Cannot copy over the '" & Options.DefaultProfileName & "' profile. Please choose another name.",
                            "Invalid Profile Name", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Exit Sub
        End If

        Options.SaveProfile(copiedProfileName)
        GetProfiles(copiedProfileName)

        preventLoadProfile = False
    End Sub

    Private Sub DeleteProfileButton_Click(sender As Object, e As EventArgs) Handles DeleteProfileButton.Click
        If ProfileComboBox.Text = Options.DefaultProfileName Then
            MessageBox.Show(Me, "Cannot delete the '" & Options.DefaultProfileName & "' profile.",
                            "Invalid Profile", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Exit Sub
        End If

        preventLoadProfile = True

        If Options.DeleteProfile(ProfileComboBox.Text) Then
            MessageBox.Show(Me, "Profile deleted successfully", "Profile Deleted", MessageBoxButtons.OK, MessageBoxIcon.Information)
        Else
            MessageBox.Show(Me, "Error attempting to delete this profile. Perhaps it has already been deleted.",
                            "Deletion Error", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
        GetProfiles(Options.DefaultProfileName)

        preventLoadProfile = False
    End Sub

    Private Sub ProfileComboBox_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ProfileComboBox.SelectedIndexChanged
        If Not preventLoadProfile Then
            Options.LoadProfile(ProfileComboBox.Text, True)
            ReloadFilterCategories()
        End If
    End Sub

    Private Sub FilterAnyRadio_CheckedChanged(sender As Object, e As EventArgs) Handles FilterAnyRadio.CheckedChanged
        If FilterAnyRadio.Checked Then
            FilterOptionsBox.Enabled = True
            RefilterSelection()
        End If
    End Sub

    Private Sub FilterExactlyRadio_CheckedChanged(sender As Object, e As EventArgs) Handles FilterExactlyRadio.CheckedChanged
        If FilterExactlyRadio.Checked Then
            FilterOptionsBox.Enabled = True
            RefilterSelection()
        End If
    End Sub

    Private Sub FilterAllRadio_CheckedChanged(sender As Object, e As EventArgs) Handles FilterAllRadio.CheckedChanged
        If FilterAllRadio.Checked AndAlso Me.Visible Then
            FilterOptionsBox.Enabled = False
            RefilterSelection()
        End If
    End Sub

    Private Sub RefilterSelection(Optional tags As HashSet(Of CaseInsensitiveString) = Nothing,
                                  Optional notTaggedChecked As Boolean? = Nothing)
        If tags Is Nothing Then tags =
            New HashSet(Of CaseInsensitiveString)(FilterOptionsBox.CheckedItems.OfType(Of CaseInsensitiveString)())
        Dim notTaggedFlag As Boolean
        If notTaggedChecked Is Nothing Then
            notTaggedFlag = FilterOptionsBox.GetItemChecked(notTaggedFilterIndex)
        Else
            notTaggedFlag = notTaggedChecked.Value
        End If

        For Each selectionControl As PonySelectionControl In PonySelectionPanel.Controls
            ' Show all ponies.
            If FilterAllRadio.Checked Then
                selectionControlFilter(selectionControl) = True
            End If

            ' Show ponies with at least one matching tag.
            If FilterAnyRadio.Checked Then
                Dim visible = selectionControl.PonyBase.Tags.Any(Function(tag) tags.Contains(tag)) OrElse
                (selectionControl.PonyBase.Tags.Count = 0 AndAlso notTaggedFlag)
                selectionControlFilter(selectionControl) = visible
            End If

            ' Show ponies which match all tags.
            If FilterExactlyRadio.Checked Then
                Dim visible = If(notTaggedFlag,
                                 selectionControl.PonyBase.Tags.Count = 0 AndAlso tags.Count = 0,
                                 selectionControl.PonyBase.Tags.IsSupersetOf(tags))
                selectionControlFilter(selectionControl) = visible
            End If
        Next

        ponyOffset = 0
        RepaginateSelection()
    End Sub

    Private Sub RepaginateSelection()
        PonySelectionPanel.SuspendLayout()

        Dim localOffset = 0
        Dim visibleCount = 0
        For Each selectionControl As PonySelectionControl In PonySelectionPanel.Controls
            Dim makeVisible = False
            If Not PaginationEnabled.Checked Then
                ' If pagination is disabled, simply show/hide the control according to the current filter.
                makeVisible = selectionControlFilter(selectionControl)
            ElseIf selectionControlFilter(selectionControl) Then
                ' If pagination is enabled, we will show it if it is filtered visible and within the page range.
                makeVisible = localOffset >= ponyOffset AndAlso visibleCount < PoniesPerPage.Value
                localOffset += 1
            End If
            If makeVisible Then visibleCount += 1
            Dim visibleChanged = selectionControl.Visible <> makeVisible
            selectionControl.Visible = makeVisible
        Next

        PonySelectionPanel.ResumeLayout()

        If Not PaginationEnabled.Checked OrElse visibleCount = 0 Then
            PonyPaginationLabel.Text = String.Format(CultureInfo.CurrentCulture, "Viewing {0} ponies", visibleCount)
        Else
            PonyPaginationLabel.Text =
            String.Format(CultureInfo.CurrentCulture,
                          "Viewing {0} to {1} of {2} ponies",
                          ponyOffset + 1,
                          Math.Min(ponyOffset + PoniesPerPage.Value, selectionControlsFilteredVisible.Count),
                          selectionControlsFilteredVisible.Count)
        End If

        Dim min = ponyOffset = 0
        Dim max = ponyOffset >= selectionControlsFilteredVisible.Count - PoniesPerPage.Value
        FirstPageButton.Enabled = Not min
        PreviousPageButton.Enabled = Not min
        PreviousPonyButton.Enabled = Not min
        NextPonyButton.Enabled = Not max
        NextPageButton.Enabled = Not max
        LastPageButton.Enabled = Not max
    End Sub

    Private Sub Main_KeyPress(sender As Object, e As KeyPressEventArgs) Handles MyBase.KeyPress
        If ProfileComboBox.Focused Then Exit Sub

        If Char.IsLetter(e.KeyChar) Then
            e.Handled = True
            For Each selectionControl In selectionControlsFilteredVisible
                If selectionControl.PonyName.Text.Length > 0 Then
                    Dim compare = String.Compare(selectionControl.PonyName.Text(0), e.KeyChar, StringComparison.OrdinalIgnoreCase)
                    If compare = 0 Then
                        PonySelectionPanel.ScrollControlIntoView(selectionControl)
                        selectionControl.PonyCount.Focus()
                        Exit For
                    End If
                End If
            Next
        ElseIf e.KeyChar = "#"c Then
            EvilGlobals.InPreviewMode = True
            Using newEditor = New PonyEditorForm2()
                newEditor.ShowDialog(Me)
                If newEditor.ChangesMade Then
                    ResetPonySelection()
                    FilterAllRadio.Checked = True
                    LoadingProgressBar.Visible = True
                    LoadInternal()
                End If
            End Using
            EvilGlobals.InPreviewMode = False
        End If
    End Sub

    Private Sub FirstPageButton_Click(sender As Object, e As EventArgs) Handles FirstPageButton.Click
        ponyOffset = 0
        RepaginateSelection()
    End Sub

    Private Sub PreviousPageButton_Click(sender As Object, e As EventArgs) Handles PreviousPageButton.Click
        ponyOffset -= Math.Min(ponyOffset, CInt(PoniesPerPage.Value))
        RepaginateSelection()
    End Sub

    Private Sub PreviousPonyButton_Click(sender As Object, e As EventArgs) Handles PreviousPonyButton.Click
        ponyOffset -= Math.Min(ponyOffset, 1)
        RepaginateSelection()
    End Sub

    Private Sub NextPonyButton_Click(sender As Object, e As EventArgs) Handles NextPonyButton.Click
        ponyOffset += Math.Min(selectionControlsFilteredVisible.Count - CInt(PoniesPerPage.Value) - ponyOffset, 1)
        RepaginateSelection()
    End Sub

    Private Sub NextPageButton_Click(sender As Object, e As EventArgs) Handles NextPageButton.Click
        ponyOffset += Math.Min(selectionControlsFilteredVisible.Count - CInt(PoniesPerPage.Value) - ponyOffset, CInt(PoniesPerPage.Value))
        RepaginateSelection()
    End Sub

    Private Sub LastPageButton_Click(sender As Object, e As EventArgs) Handles LastPageButton.Click
        ponyOffset = selectionControlsFilteredVisible.Count - CInt(PoniesPerPage.Value)
        RepaginateSelection()
    End Sub

    Private Sub PoniesPerPage_ValueChanged(sender As Object, e As EventArgs) Handles PoniesPerPage.ValueChanged
        If initialized Then RepaginateSelection()
    End Sub

    Private Sub PaginationEnabled_CheckedChanged(sender As Object, e As EventArgs) Handles PaginationEnabled.CheckedChanged
        PonyPaginationPanel.Enabled = PaginationEnabled.Checked
        RepaginateSelection()
    End Sub

    Private Sub FilterOptionsBox_ItemCheck(sender As Object, e As ItemCheckEventArgs) Handles FilterOptionsBox.ItemCheck
        Dim tags = New HashSet(Of CaseInsensitiveString)(FilterOptionsBox.CheckedItems.OfType(Of CaseInsensitiveString)())
        Dim notTaggedChecked As Boolean?
        If e.CurrentValue <> e.NewValue Then
            If e.Index <> notTaggedFilterIndex Then
                Dim changedTag = DirectCast(FilterOptionsBox.Items(e.Index), CaseInsensitiveString)
                If e.NewValue = CheckState.Checked Then
                    tags.Add(changedTag)
                Else
                    tags.Remove(changedTag)
                End If
            Else
                notTaggedChecked = e.NewValue = CheckState.Checked
            End If
        End If
        RefilterSelection(tags, notTaggedChecked)
    End Sub
#End Region

#Region "Pony Startup"
    Private Sub GoButton_Click(sender As Object, e As EventArgs) Handles GoButton.Click
        LoadPonies()
    End Sub

    Private Sub LoadPonies()
        loading = True
        SelectionControlsPanel.Enabled = False
        LoadingProgressBar.Visible = True
        loadWatch.Restart()
        Threading.ThreadPool.QueueUserWorkItem(AddressOf LoadPoniesAsync)
    End Sub

    Private Sub LoadPoniesAsync(o As Object)
        Try
            ' Note down the number of each pony that is wanted.
            Dim totalPonies As Integer
            Dim ponyBasesWanted As New List(Of Tuple(Of String, Integer))()
            For Each ponyPanel As PonySelectionControl In PonySelectionPanel.Controls
                Dim ponyName = ponyPanel.PonyName.Text
                Dim count As Integer
                If Integer.TryParse(ponyPanel.PonyCount.Text, count) AndAlso count > 0 Then
                    ponyBasesWanted.Add(Tuple.Create(ponyName, count))
                    totalPonies += count
                End If
            Next

            If totalPonies = 0 Then
                If EvilGlobals.InScreensaverMode Then
                    ponyBasesWanted.Add(Tuple.Create(PonyBase.RandomDirectory, 1))
                    totalPonies = 1
                Else
                    LoadPoniesAsyncEnd(
                        True,
                        Sub()
                            MessageBox.Show(Me, "You haven't selected any ponies! Choose some ponies to roam your desktop first.",
                                            "No Ponies Selected", MessageBoxButtons.OK, MessageBoxIcon.Information)
                        End Sub)
                    Return
                End If
            End If

            If totalPonies > Options.MaxPonyCount Then
                LoadPoniesAsyncEnd(
                    True,
                    Sub()
                        MessageBox.Show(Me, String.Format(
                            CultureInfo.CurrentCulture,
                            "Sorry you selected {1} ponies, which is more than the limit specified in the options menu.{0}" &
                            "Try choosing no more than {2} in total.{0}" &
                            "(or, you can increase the limit via the options menu)",
                            Environment.NewLine, totalPonies, Options.MaxPonyCount),
                        "Too Many Ponies", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    End Sub)
                Return
            End If

            ' Create the initial set of ponies to start.
            startupPonies.Clear()
            Dim randomPoniesWanted As Integer
            For Each ponyBaseWanted In ponyBasesWanted
                If ponyBaseWanted.Item1 = PonyBase.RandomDirectory Then
                    randomPoniesWanted = ponyBaseWanted.Item2
                    Continue For
                End If
                Dim base = ponies.Bases.Single(Function(ponyBase) ponyBase.Directory = ponyBaseWanted.Item1)
                ' Add the designated amount of a given pony.
                For i = 1 To ponyBaseWanted.Item2
                    startupPonies.Add(New Pony(base))
                Next
            Next

            ' Add a random amount of ponies.
            If randomPoniesWanted > 0 Then
                Dim remainingPonyBases = ponies.Bases.ToList()
                If Options.NoRandomDuplicates Then
                    remainingPonyBases.RemoveAll(Function(pb) ponyBasesWanted.Any(Function(t) t.Item1 = pb.Directory))
                End If
                For i = 1 To randomPoniesWanted
                    If remainingPonyBases.Count = 0 Then Exit For
                    Dim index = Rng.Next(remainingPonyBases.Count)
                    startupPonies.Add(New Pony(remainingPonyBases(index)))
                    If Options.NoRandomDuplicates Then
                        remainingPonyBases.RemoveAt(index)
                    End If
                Next
            End If

            PonyStartup()
            LoadPoniesAsyncEnd(False)
        Catch ex As Exception
            Program.NotifyUserOfNonFatalException(ex, "Error attempting to launch ponies.")
            LoadPoniesAsyncEnd(True)
#If DEBUG Then
            Throw
#End If
        End Try
    End Sub

    Private Sub PonyStartup()
        If EvilGlobals.InScreensaverMode Then SmartInvoke(AddressOf CreateScreensaverForms)

        AddHandlerDisplaySettingsChanged(AddressOf ReturnToMenuOnResolutionChange)
        ponyViewer = Options.GetInterface()
        ponyViewer.Topmost = Options.AlwaysOnTop
        If TypeOf ponyViewer Is WinFormSpriteInterface Then
            DirectCast(ponyViewer, WinFormSpriteInterface).ShowPerformanceGraph = Options.ShowPerformanceGraph
        End If

        If Not EvilGlobals.InPreviewMode Then
            ' Get a collection of all images to be loaded.
            Dim images As New HashSet(Of String)(PathEquality.Comparer)
            For Each pony In startupPonies
                For Each behavior In pony.Behaviors
                    images.Add(behavior.LeftImage.Path)
                    images.Add(behavior.RightImage.Path)
                    For Each effect In behavior.Effects
                        images.Add(effect.LeftImage.Path)
                        images.Add(effect.RightImage.Path)
                    Next
                Next
            Next

            worker.QueueTask(Sub()
                                 LoadingProgressBar.Value = 0
                                 LoadingProgressBar.Maximum = images.Count
                             End Sub)
            ponyViewer.LoadImages(images, Sub() worker.QueueTask(Sub() LoadingProgressBar.Value += 1))
        End If

        animator = New DesktopPonyAnimator(ponyViewer, startupPonies, ponies)
        AddHandler animator.AnimationFinished, Sub() Threading.ThreadPool.QueueUserWorkItem(
                                                   Sub() SmartInvoke(
                                                       Sub()
                                                           Dim exitRequest = animator.ExitRequested
                                                           PonyShutdown()
                                                           If exitRequest = exitRequest.ExitApplication Then
                                                               Close()
                                                           Else
                                                               Show()
                                                               General.FullCollect()
                                                           End If
                                                       End Sub))

        EvilGlobals.CurrentViewer = ponyViewer
        EvilGlobals.CurrentAnimator = animator
    End Sub

    Private Sub CreateScreensaverForms()
        If Options.ScreensaverStyle <> Options.ScreensaverBackgroundStyle.Transparent Then
            screensaverForms = New List(Of ScreensaverBackgroundForm)()

            Dim backgroundColor As Color = Color.Black
            Dim backgroundImage As Image = Nothing
            If Options.ScreensaverStyle = Options.ScreensaverBackgroundStyle.SolidColor Then
                backgroundColor = Color.FromArgb(255, Options.ScreensaverBackgroundColor)
            End If
            If Options.ScreensaverStyle = Options.ScreensaverBackgroundStyle.BackgroundImage Then
                Try
                    backgroundImage = Image.FromFile(Options.ScreensaverBackgroundImagePath)
                Catch
                    ' Image failed to load, so we'll fall back to a background color.
                End Try
            End If

            For Each monitor In Screen.AllScreens
                Dim screensaverBackground As New ScreensaverBackgroundForm()
                screensaverForms.Add(screensaverBackground)

                If backgroundImage IsNot Nothing Then
                    screensaverBackground.BackgroundImage = backgroundImage
                Else
                    screensaverBackground.BackColor = backgroundColor
                End If

                screensaverBackground.Size = monitor.Bounds.Size
                screensaverBackground.Location = monitor.Bounds.Location

                screensaverBackground.Show()
            Next
        End If
        Cursor.Hide()
    End Sub

    Private Sub ReturnToMenuOnResolutionChange(sender As Object, e As EventArgs)
        If Not Disposing AndAlso Not IsDisposed Then
            SmartInvoke(Sub()
                            PonyShutdown()
                            MessageBox.Show(Me, "You will be returned to the menu because your screen resolution has changed.",
                                            "Resolution Changed - Desktop Ponies", MessageBoxButtons.OK, MessageBoxIcon.Information)
                            Show()
                        End Sub)
        End If
    End Sub

    Private Sub LoadPoniesAsyncEnd(cancelled As Boolean, Optional uiAction As Action = Nothing)
        worker.QueueTask(
            Sub()
                Dim totalImages = LoadingProgressBar.Maximum

                LoadingProgressBar.Value = 0
                LoadingProgressBar.Maximum = 1
                If Not Runtime.IsMono Then LoadingProgressBar.Visible = False

                If uiAction IsNot Nothing Then uiAction()
                SelectionControlsPanel.Enabled = True

                loading = False
                If Not cancelled Then
                    EvilGlobals.PoniesHaveLaunched = True
                    TempSaveCounts()
                    Visible = False
                    animator.Start()
                    loadWatch.Stop()
                    Console.WriteLine("Loaded in {0:0.00s} ({1} images)", loadWatch.Elapsed.TotalSeconds, totalImages)
                End If
            End Sub)
    End Sub
#End Region

    Private Sub PonySelectionPanel_Resize(sender As Object, e As EventArgs) Handles PonySelectionPanel.Resize
        ' If a horizontal scrollbar has appeared, renew the layout to forcibly remove it.
        If PonySelectionPanel.HorizontalScroll.Visible Then
            PonySelectionPanel.SuspendLayout()
            For Each selectionControl As PonySelectionControl In PonySelectionPanel.Controls
                selectionControl.Visible = False
            Next
            PonySelectionPanel.ResumeLayout()
            ' Perform a layout so cached positions are cleared, then restore visibility to its previous state.
            RepaginateSelection()
        End If
    End Sub

    Private Sub PonyShutdown()
        RemoveHandlerDisplaySettingsChanged(AddressOf ReturnToMenuOnResolutionChange)

        If animator IsNot Nothing Then animator.Finish()
        EvilGlobals.PoniesHaveLaunched = False
        If animator IsNot Nothing Then animator.Clear()

        If EvilGlobals.CurrentGame IsNot Nothing Then
            EvilGlobals.CurrentGame.CleanUp()
            EvilGlobals.CurrentGame = Nothing
        End If

        If screensaverForms IsNot Nothing Then
            For Each screensaverForm In screensaverForms
                screensaverForm.Dispose()
            Next
            screensaverForms = Nothing
        End If

        If Object.ReferenceEquals(animator, EvilGlobals.CurrentAnimator) Then
            EvilGlobals.CurrentAnimator = Nothing
        End If
        animator = Nothing

        If ponyViewer IsNot Nothing Then
            ponyViewer.Close()
            If Object.ReferenceEquals(ponyViewer, EvilGlobals.CurrentViewer) Then
                EvilGlobals.CurrentViewer = Nothing
            End If
            ponyViewer = Nothing
        End If
    End Sub

    ''' <summary>
    ''' Save pony counts so they are preserved through clicking on and off filters.
    ''' </summary>
    Private Sub TempSaveCounts()
        If PonySelectionPanel.Controls.Count = 0 Then Exit Sub

        Dim newCounts = New Dictionary(Of String, Integer)()
        For Each ponyPanel As PonySelectionControl In PonySelectionPanel.Controls
            Dim count As Integer
            Integer.TryParse(ponyPanel.PonyCount.Text, count)
            newCounts.Add(ponyPanel.PonyBase.Directory, count)
        Next
        Options.PonyCounts = newCounts.AsReadOnly()
    End Sub

    ''' <summary>
    ''' Resets pony selection related controls, which will require them to be reloaded from disk.
    ''' </summary>
    Private Sub ResetPonySelection()
        ponies = Nothing
        SelectionControlsPanel.Enabled = False
        selectionControlFilter.Clear()
        PonySelectionPanel.SuspendLayout()
        For Each ponyPanel As PonySelectionControl In PonySelectionPanel.Controls
            ponyPanel.Dispose()
        Next
        PonySelectionPanel.Controls.Clear()
        PonySelectionPanel.ResumeLayout()
    End Sub

    Friend Sub ReloadFilterCategories()
        FilterOptionsBox.SuspendLayout()
        FilterOptionsBox.Items.Clear()
        FilterOptionsBox.Items.AddRange(PonyBase.StandardTags.Concat(Options.CustomTags).ToArray())
        notTaggedFilterIndex = FilterOptionsBox.Items.Add("[Not Tagged]")
        FilterOptionsBox.ResumeLayout()
        RefilterSelection()
    End Sub

    Private Sub Main_LocationChanged(sender As Object, e As EventArgs) Handles MyBase.LocationChanged
        ' If we have just returned from the minimized state, the flow panel will have an incorrect scrollbar.
        ' Force a layout to get the bar re-evaluated and fixed.
        If oldWindowState = FormWindowState.Minimized AndAlso WindowState <> FormWindowState.Minimized Then
            layoutPendingFromRestore = True
        End If
        oldWindowState = WindowState
    End Sub

    Private Sub PonySelectionPanel_Paint(sender As Object, e As PaintEventArgs) Handles PonySelectionPanel.Paint
        If layoutPendingFromRestore Then
            PonySelectionPanel.PerformLayout()
            layoutPendingFromRestore = False
        End If
    End Sub

    Private Sub Main_Activated(sender As Object, e As EventArgs) Handles MyBase.Activated
        AnimationTimer.Enabled = Not loading
    End Sub

    Private Sub Main_Deactivate(sender As Object, e As EventArgs) Handles MyBase.Deactivate
        AnimationTimer.Enabled = False
    End Sub

    Private Sub AnimationTimer_Tick(sender As Object, e As EventArgs) Handles AnimationTimer.Tick
        For Each selectionControl As PonySelectionControl In PonySelectionPanel.Controls
            selectionControl.AdvanceTimeIndex(TimeSpan.FromMilliseconds(AnimationTimer.Interval))
        Next
    End Sub

    <Security.Permissions.PermissionSet(Security.Permissions.SecurityAction.Demand, Name:="FullTrust")>
    Private Shared Sub AddHandlerDisplaySettingsChanged(handler As EventHandler)
        AddHandler Microsoft.Win32.SystemEvents.DisplaySettingsChanged, handler
    End Sub

    <Security.Permissions.PermissionSet(Security.Permissions.SecurityAction.Demand, Name:="FullTrust")>
    Private Shared Sub RemoveHandlerDisplaySettingsChanged(handler As EventHandler)
        RemoveHandler Microsoft.Win32.SystemEvents.DisplaySettingsChanged, handler
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            RemoveHandlerDisplaySettingsChanged(AddressOf ReturnToMenuOnResolutionChange)
            If disposing Then
                If components IsNot Nothing Then components.Dispose()
                If animator IsNot Nothing Then animator.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub
End Class
