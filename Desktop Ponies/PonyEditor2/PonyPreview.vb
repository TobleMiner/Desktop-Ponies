﻿Imports DesktopSprites.SpriteManagement
Imports System.Globalization

Public Class PonyPreview
    Private loaded As Boolean
    Private editorForm As PonyEditorForm2
    Private ponies As PonyCollection
    Private editorAnimator As Editor2PonyAnimator
    Private editorInterface As ISpriteCollectionView
    Private previewPony As Pony
    Private previewPonyReady As Boolean
    Private ReadOnly previewPonyGuard As New Object()
    Private ReadOnly parents As New List(Of Control)()
    Private determineLocationOnPaint As Boolean
    Private disposedFlag As Integer

    Public Event PreviewFocused As EventHandler
    Public Event PreviewUnfocused As EventHandler

    Public Sub New(editorForm As PonyEditorForm2, ponies As PonyCollection)
        Me.editorForm = Argument.EnsureNotNull(editorForm, "editorForm")
        Me.ponies = Argument.EnsureNotNull(ponies, "ponies")
        InitializeComponent()
        AddHandler Disposed, Sub()
                                 Threading.Interlocked.Exchange(disposedFlag, 1)
                                 If editorAnimator IsNot Nothing Then editorAnimator.Finish()
                                 EvilGlobals.CurrentAnimator = Nothing
                             End Sub
    End Sub

    Private Sub DetermineParentsAndScreenLocation(sender As Object, e As EventArgs)
        For Each oldParent In parents
            RemoveHandler oldParent.LocationChanged, AddressOf DetermineScreenLocation
            RemoveHandler oldParent.SizeChanged, AddressOf DetermineScreenLocation
            RemoveHandler oldParent.ParentChanged, AddressOf DetermineParentsAndScreenLocation
        Next
        parents.Clear()
        Dim newParent As Control = PreviewPanel
        While newParent IsNot Nothing
            parents.Add(newParent)
            AddHandler newParent.LocationChanged, AddressOf DetermineScreenLocation
            AddHandler newParent.SizeChanged, AddressOf DetermineScreenLocation
            AddHandler newParent.ParentChanged, AddressOf DetermineParentsAndScreenLocation
            newParent = newParent.Parent
        End While
        If TypeOf parents(parents.Count - 1) Is Form Then
            DetermineScreenLocation(Me, EventArgs.Empty)
        Else
            determineLocationOnPaint = True
        End If
    End Sub

    Private Sub DetermineScreenLocation(sender As Object, e As EventArgs)
        Dim bounds = PreviewPanel.RectangleToScreen(PreviewPanel.ClientRectangle)
        EvilGlobals.PreviewWindowRectangle = bounds
        If TypeOf editorInterface Is WinFormSpriteInterface Then
            DirectCast(editorInterface, WinFormSpriteInterface).DisplayBounds = bounds
        End If
    End Sub

    Private Sub PreviewPanel_Paint(sender As Object, e As PaintEventArgs) Handles PreviewPanel.Paint
        If determineLocationOnPaint Then
            DetermineScreenLocation(Me, EventArgs.Empty)
            determineLocationOnPaint = False
        End If
    End Sub

    Private Sub PonyPreview_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        If loaded Then Return
        editorInterface = Options.GetInterface()
        editorInterface.Topmost = True
        AddHandler editorInterface.Focused, Sub() RaiseEvent PreviewFocused(Me, EventArgs.Empty)
        AddHandler editorInterface.Unfocused, Sub() RaiseEvent PreviewUnfocused(Me, EventArgs.Empty)
        DetermineParentsAndScreenLocation(Me, EventArgs.Empty)
        editorAnimator = New Editor2PonyAnimator(editorInterface, ponies, Me)
        EvilGlobals.CurrentAnimator = editorAnimator
        editorAnimator.Start()
        loaded = True
    End Sub

    Public Sub RestartForPony(base As PonyBase, Optional startBehavior As Behavior = Nothing)
        If Not Created Then CreateControl()
        editorAnimator.Pause(True)
        editorAnimator.Clear()
        SyncLock previewPonyGuard
            previewPonyReady = False
            previewPony = New Pony(base)
        End SyncLock
        editorAnimator.AddPonyNotify(previewPony, Sub(pony) HandleAddedNotification(pony, startBehavior))
        PonyNameValueLabel.Text = base.Directory
        BehaviorNameValueLabel.Text = ""
        TimeLeftValueLabel.Text = ""
    End Sub

    Private Sub HandleAddedNotification(addedPony As Pony, startBehavior As Behavior)
        SyncLock previewPonyGuard
            If Object.ReferenceEquals(addedPony, previewPony) Then
                previewPonyReady = True
                editorAnimator.ChangeEditorMenu(previewPony.Base)
                If startBehavior IsNot Nothing Then
                    previewPony.SelectBehavior(startBehavior)
                End If
            End If
        End SyncLock
    End Sub

    Public Function ShowDialogOverPreview(show As Func(Of DialogResult)) As DialogResult
        Dim wasVisible = editorAnimator IsNot Nothing AndAlso Not editorAnimator.Paused
        If wasVisible Then HidePreview()
        Dim result = show()
        If wasVisible Then ShowPreview()
        Return result
    End Function

    Public Sub RunBehavior(behavior As Behavior)
        previewPony.SelectBehavior(behavior)
        If Not Object.ReferenceEquals(previewPony.CurrentBehavior, behavior) Then
            ShowDialogOverPreview(
                Function() MessageBox.Show(Me, "Couldn't run this behavior. Maybe images are missing or it is not set up correctly.",
                                           "Run Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning))
        End If
    End Sub

    Public Sub HidePreview()
        If editorAnimator IsNot Nothing Then editorAnimator.Pause(True)
    End Sub

    Public Sub ShowPreview()
        If editorAnimator IsNot Nothing Then editorAnimator.Resume()
    End Sub

    Public ReadOnly Property PreviewVisible As Boolean
        Get
            Return editorAnimator IsNot Nothing AndAlso Not editorAnimator.Paused
        End Get
    End Property

    Public ReadOnly Property PreviewHasFocus As Boolean
        Get
            Return editorInterface IsNot Nothing AndAlso editorInterface.HasFocus
        End Get
    End Property

    Public Sub AnimatorStart()
        If disposedFlag = 1 Then Return
        Try
            BeginInvoke(New EventHandler(AddressOf DetermineScreenLocation))
        Catch ex As InvalidOperationException
            If disposedFlag <> 1 Then Throw
        End Try
    End Sub

    Public Sub AnimatorUpdate()
        If disposedFlag = 1 Then Return
        Try
            BeginInvoke(New MethodInvoker(
                Sub()
                    SyncLock previewPonyGuard
                        If previewPony Is Nothing OrElse Not previewPonyReady Then Return
                        BehaviorNameValueLabel.Text =
                            If(previewPony.CurrentBehavior Is Nothing, "", previewPony.CurrentBehavior.Name.ToString())
                        TimeLeftValueLabel.Text =
                            (previewPony.BehaviorDesiredDuration - previewPony.ImageTimeIndex).
                            TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture)
                    End SyncLock
                End Sub))
        Catch ex As InvalidOperationException
            If disposedFlag <> 1 Then Throw
        End Try
    End Sub
End Class
