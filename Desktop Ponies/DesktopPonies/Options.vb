﻿Imports System.Globalization
Imports System.IO
Imports System.Text

Public NotInheritable Class Options
    Public Shared ReadOnly Property DefaultProfileName As CaseInsensitiveString
        Get
            Return "default"
        End Get
    End Property

    Private Const OptionsCount = 30
    Public Shared ProfileName As String

    Public Shared SuspendForFullscreenApplication As Boolean
    Public Shared ShowInTaskbar As Boolean
    Public Shared AlwaysOnTop As Boolean
    Private Shared alphaBlendingEnabled As Boolean
    Public Shared WindowAvoidanceEnabled As Boolean
    Public Shared CursorAvoidanceEnabled As Boolean
    Public Shared CursorAvoidanceSize As Single
    Public Shared SoundEnabled As Boolean
    Public Shared SoundVolume As Single
    Public Shared SoundSingleChannelOnly As Boolean

    Public Shared PonyAvoidsPonies As Boolean
    Public Shared PonyStaysInBox As Boolean
    Public Shared PonyEffectsEnabled As Boolean
    Public Shared PonyDraggingEnabled As Boolean
    Public Shared PonyTeleportEnabled As Boolean
    Public Shared PonySpeechEnabled As Boolean
    Public Shared PonySpeechChance As Single
    Public Shared PonyInteractionsExist As Boolean
    Public Shared PonyInteractionsEnabled As Boolean
    Public Shared DisplayPonyInteractionsErrors As Boolean

    Public Shared ScreensaverSoundEnabled As Boolean
    Public Shared ScreensaverStyle As ScreensaverBackgroundStyle
    Public Shared ScreensaverBackgroundColor As Color
    Public Shared ScreensaverBackgroundImagePath As String = ""

    Public Shared NoRandomDuplicates As Boolean

    Public Shared MaxPonyCount As Integer
    Public Shared TimeFactor As Single
    Public Shared ScaleFactor As Single
    Public Shared ExclusionZone As RectangleF

    Public Shared Screens As ImmutableArray(Of Screen)
    Public Shared PonyCounts As ReadOnlyDictionary(Of String, Integer)
    Public Shared CustomTags As ImmutableArray(Of CaseInsensitiveString)

    Public Shared EnablePonyLogs As Boolean
    Public Shared ShowPerformanceGraph As Boolean

    Public Shared ReadOnly Property ProfileDirectory As String
        Get
            Return Path.Combine(EvilGlobals.InstallLocation, "Profiles")
        End Get
    End Property

    Public Enum ScreensaverBackgroundStyle
        Transparent
        SolidColor
        BackgroundImage
    End Enum

    Shared Sub New()
        LoadDefaultProfile()
    End Sub

    Private Sub New()
    End Sub

    Private Shared Sub ValidateProfileName(profile As String)
        If String.IsNullOrEmpty(profile) Then Throw New ArgumentException("profile must not be null or empty.", "profile")
        If profile = DefaultProfileName Then
            Throw New ArgumentException("profile must not match the default profile name.", "profile")
        End If
    End Sub

    Public Shared Function GetKnownProfiles() As String()
        Try
            Dim files = Directory.GetFiles(ProfileDirectory, "*.ini", SearchOption.TopDirectoryOnly)
            For i = 0 To files.Length - 1
                files(i) = files(i).Replace(ProfileDirectory & Path.DirectorySeparatorChar, "").Replace(".ini", "")
            Next
            Return files
        Catch ex As DirectoryNotFoundException
            ' Screensaver mode set up a bad path, and we couldn't find what we needed.
            Return Nothing
        End Try
    End Function

    Public Shared Sub LoadProfile(profile As String, setAsCurrent As Boolean)
        Argument.EnsureNotNullOrEmpty(profile, "profile")

        If profile = DefaultProfileName Then
            LoadDefaultProfile()
        Else
            Using reader As New StreamReader(Path.Combine(ProfileDirectory, profile & ".ini"), Encoding.UTF8)
                ProfileName = profile
                Dim newScreens As New List(Of Screen)()
                Dim newCounts As New Dictionary(Of String, Integer)()
                Dim newTags As New List(Of CaseInsensitiveString)()
                While Not reader.EndOfStream
                    Dim columns = CommaSplitQuoteQualified(reader.ReadLine())
                    If columns.Length = 0 Then Continue While

                    Select Case columns(0)
                        Case "options"
                            ' TODO: Change to a lenient parser.
                            If columns.Length - 1 < OptionsCount Then Throw New InvalidDataException(
                                String.Format(CultureInfo.CurrentCulture, "Expected at least {0} options on the options line.", OptionsCount))
                            PonySpeechEnabled = Boolean.Parse(columns(1))
                            PonySpeechChance = Single.Parse(columns(2), CultureInfo.InvariantCulture)
                            CursorAvoidanceEnabled = Boolean.Parse(columns(3))
                            CursorAvoidanceSize = Single.Parse(columns(4), CultureInfo.InvariantCulture)
                            PonyDraggingEnabled = Boolean.Parse(columns(5))
                            PonyInteractionsEnabled = Boolean.Parse(columns(6))
                            DisplayPonyInteractionsErrors = Boolean.Parse(columns(7))
                            ExclusionZone.X = Single.Parse(columns(8), CultureInfo.InvariantCulture)
                            ExclusionZone.Y = Single.Parse(columns(9), CultureInfo.InvariantCulture)
                            ExclusionZone.Width = Single.Parse(columns(10), CultureInfo.InvariantCulture)
                            ExclusionZone.Height = Single.Parse(columns(11), CultureInfo.InvariantCulture)
                            ScaleFactor = Single.Parse(columns(12), CultureInfo.InvariantCulture)
                            MaxPonyCount = Integer.Parse(columns(13), CultureInfo.InvariantCulture)
                            alphaBlendingEnabled = Boolean.Parse(columns(14))
                            PonyEffectsEnabled = Boolean.Parse(columns(15))
                            WindowAvoidanceEnabled = Boolean.Parse(columns(16))
                            PonyAvoidsPonies = Boolean.Parse(columns(17))
                            PonyStaysInBox = Boolean.Parse(columns(18))
                            PonyTeleportEnabled = Boolean.Parse(columns(19))
                            TimeFactor = Single.Parse(columns(20), CultureInfo.InvariantCulture)
                            SoundEnabled = Boolean.Parse(columns(21))
                            SoundSingleChannelOnly = Boolean.Parse(columns(22))
                            SoundVolume = Single.Parse(columns(23), CultureInfo.InvariantCulture)
                            AlwaysOnTop = Boolean.Parse(columns(24))
                            SuspendForFullscreenApplication = Boolean.Parse(columns(25))
                            ScreensaverSoundEnabled = Boolean.Parse(columns(26))
                            ScreensaverStyle = CType([Enum].Parse(GetType(ScreensaverBackgroundStyle), columns(27)), 
                                ScreensaverBackgroundStyle)
                            ScreensaverBackgroundColor = Color.FromArgb(Integer.Parse(columns(28), CultureInfo.InvariantCulture))
                            ScreensaverBackgroundImagePath = columns(29)
                            NoRandomDuplicates = Boolean.Parse(columns(30))
                        Case "monitor"
                            If columns.Length - 1 <> 1 Then Throw New InvalidDataException("Expected a monitor name on the monitor line.")
                            Dim monitor = Screen.AllScreens.FirstOrDefault(Function(s) s.DeviceName = columns(1))
                            If monitor IsNot Nothing Then newScreens.Add(monitor)
                        Case "count"
                            If columns.Length - 1 <> 2 Then Throw New InvalidDataException("Expected a count on the count line.")
                            newCounts.Add(columns(1), Integer.Parse(columns(2), CultureInfo.InvariantCulture))
                        Case "tag"
                            If columns.Length - 1 <> 1 Then Throw New InvalidDataException("Expected a tag name on the tag line.")
                            newTags.Add(columns(1))
                    End Select
                End While
                Screens = newScreens.ToImmutableArray()
                PonyCounts = newCounts.AsReadOnly()
                CustomTags = newTags.ToImmutableArray()
            End Using
        End If

        LoadPonyCounts()

        If setAsCurrent Then
            Try
                IO.File.WriteAllText(IO.Path.Combine(Options.ProfileDirectory, "current.txt"), profile, System.Text.Encoding.UTF8)
            Catch ex As IO.IOException
                ' If we cannot write out the file that remembers the last used profile, that is unfortunate but not a fatal problem.
                Console.WriteLine("Warning: Failed to save current.txt file.")
            End Try
        End If
    End Sub

    Public Shared Sub LoadDefaultProfile()
        ProfileName = DefaultProfileName
        Screens = {Screen.PrimaryScreen}.ToImmutableArray()
        PonyCounts = New Dictionary(Of String, Integer)().AsReadOnly()
        CustomTags = New CaseInsensitiveString() {}.ToImmutableArray()

        SuspendForFullscreenApplication = True
        ShowInTaskbar = OperatingSystemInfo.IsWindows
        AlwaysOnTop = True
        alphaBlendingEnabled = True
        WindowAvoidanceEnabled = False
        CursorAvoidanceEnabled = True
        CursorAvoidanceSize = 100
        SoundEnabled = True
        SoundVolume = 0.75
        SoundSingleChannelOnly = False

        PonyAvoidsPonies = False
        PonyStaysInBox = False
        PonyEffectsEnabled = True
        PonyDraggingEnabled = True
        PonyTeleportEnabled = False
        PonySpeechEnabled = True
        PonySpeechChance = 0.01
        PonyInteractionsExist = False
        PonyInteractionsEnabled = True
        DisplayPonyInteractionsErrors = False

        ScreensaverSoundEnabled = True
        ScreensaverStyle = ScreensaverBackgroundStyle.Transparent
        ScreensaverBackgroundColor = Color.Empty
        ScreensaverBackgroundImagePath = ""

        NoRandomDuplicates = True

        MaxPonyCount = 300
        TimeFactor = 1
        ScaleFactor = 1
        ExclusionZone = RectangleF.Empty

        EnablePonyLogs = False
        ShowPerformanceGraph = False
    End Sub

    Public Shared Sub LoadPonyCounts()
        If EvilGlobals.PoniesHaveLaunched Then Exit Sub

        For Each ponyPanel As PonySelectionControl In EvilGlobals.Main.PonySelectionPanel.Controls
            If PonyCounts.ContainsKey(ponyPanel.PonyName.Text) Then
                ponyPanel.Count = PonyCounts(ponyPanel.PonyBase.Directory)
            Else
                ponyPanel.Count = 0
            End If
        Next
    End Sub

    Public Shared Sub SaveProfile(profile As String)
        ValidateProfileName(profile)

        Using file As New StreamWriter(Path.Combine(ProfileDirectory, profile & ".ini"), False, Encoding.UTF8)
            Dim optionsLine = String.Join(",", "options",
                                     PonySpeechEnabled,
                                     PonySpeechChance.ToString(CultureInfo.InvariantCulture),
                                     CursorAvoidanceEnabled,
                                     CursorAvoidanceSize.ToString(CultureInfo.InvariantCulture),
                                     PonyDraggingEnabled,
                                     PonyInteractionsEnabled,
                                     DisplayPonyInteractionsErrors,
                                     ExclusionZone.X.ToString(CultureInfo.InvariantCulture),
                                     ExclusionZone.Y.ToString(CultureInfo.InvariantCulture),
                                     ExclusionZone.Width.ToString(CultureInfo.InvariantCulture),
                                     ExclusionZone.Height.ToString(CultureInfo.InvariantCulture),
                                     ScaleFactor.ToString(CultureInfo.InvariantCulture),
                                     MaxPonyCount.ToString(CultureInfo.InvariantCulture),
                                     alphaBlendingEnabled,
                                     PonyEffectsEnabled,
                                     WindowAvoidanceEnabled,
                                     PonyAvoidsPonies,
                                     PonyStaysInBox,
                                     PonyTeleportEnabled,
                                     TimeFactor.ToString(CultureInfo.InvariantCulture),
                                     SoundEnabled,
                                     SoundSingleChannelOnly,
                                     SoundVolume.ToString(CultureInfo.InvariantCulture),
                                     AlwaysOnTop,
                                     SuspendForFullscreenApplication,
                                     ScreensaverSoundEnabled,
                                     ScreensaverStyle,
                                     ScreensaverBackgroundColor.ToArgb().ToString(CultureInfo.InvariantCulture),
                                     ScreensaverBackgroundImagePath,
                                     NoRandomDuplicates)
            file.WriteLine(optionsLine)

            GetPonyCounts()

            For Each screen In Screens
                file.WriteLine(String.Join(",", "monitor", Quoted(screen.DeviceName)))
            Next

            For Each entry In PonyCounts
                file.WriteLine(String.Join(",", "count", Quoted(entry.Key), entry.Value.ToString(CultureInfo.InvariantCulture)))
            Next

            For Each tag In CustomTags
                file.WriteLine(String.Join(",", "tag", Quoted(tag)))
            Next
        End Using
    End Sub

    Public Shared Function DeleteProfile(profile As String) As Boolean
        ValidateProfileName(profile)
        Try
            File.Delete(Path.Combine(ProfileDirectory, profile & ".ini"))
            Return True
        Catch
            Return False
        End Try
    End Function

    Public Shared Function ExclusionZoneForBounds(bounds As Rectangle) As Rectangle
        Return New Rectangle(CInt(ExclusionZone.X * bounds.Width + bounds.X),
                             CInt(ExclusionZone.Y * bounds.Height + bounds.Y),
                             CInt(ExclusionZone.Width * bounds.Width),
                             CInt(ExclusionZone.Height * bounds.Height))
    End Function

    Private Shared Sub GetPonyCounts()
        Dim newCounts = New Dictionary(Of String, Integer)()
        For Each ponyPanel As PonySelectionControl In EvilGlobals.Main.PonySelectionPanel.Controls
            newCounts.Add(ponyPanel.PonyBase.Directory, ponyPanel.Count)
        Next
        PonyCounts = newCounts.AsReadOnly()
    End Sub

    Public Shared Function GetCombinedScreenArea() As Rectangle
        Dim area As Rectangle = Rectangle.Empty
        For Each screen In Screens
            If area = Rectangle.Empty Then
                area = screen.WorkingArea
            Else
                area = Rectangle.Union(area, screen.WorkingArea)
            End If
        Next
        Return area
    End Function

    Public Shared Function GetInterface() As DesktopSprites.SpriteManagement.ISpriteCollectionView
        'This should already be set in the options, but in case it isn't, use all monitors.
        If Screens.Count = 0 Then Screens = Screen.AllScreens.ToImmutableArray()

        Dim viewer As DesktopSprites.SpriteManagement.ISpriteCollectionView
        If GetInterfaceType() = GetType(DesktopSprites.SpriteManagement.WinFormSpriteInterface) Then
            viewer = New DesktopSprites.SpriteManagement.WinFormSpriteInterface(GetCombinedScreenArea())
        Else
            viewer = New DesktopSprites.SpriteManagement.GtkSpriteInterface()
        End If
        viewer.ShowInTaskbar = ShowInTaskbar
        Return viewer
    End Function

    Public Shared Function GetInterfaceType() As Type
        If OperatingSystemInfo.IsWindows AndAlso Not Runtime.IsMono Then
            Return GetType(DesktopSprites.SpriteManagement.WinFormSpriteInterface)
        Else
            Return GetType(DesktopSprites.SpriteManagement.GtkSpriteInterface)
        End If
    End Function

End Class
