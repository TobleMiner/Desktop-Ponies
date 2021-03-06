﻿Imports System.IO

Public Class PonyCollection
    Private _bases As ImmutableArray(Of PonyBase)
    Public ReadOnly Property Bases As ImmutableArray(Of PonyBase)
        Get
            Return _bases
        End Get
    End Property
    Private _randomBase As PonyBase
    Public ReadOnly Property RandomBase As PonyBase
        Get
            Return _randomBase
        End Get
    End Property
    Private _houses As ImmutableArray(Of HouseBase)
    Public ReadOnly Property Houses As ImmutableArray(Of HouseBase)
        Get
            Return _houses
        End Get
    End Property
    Private ReadOnly _interactions As New Dictionary(Of String, List(Of InteractionBase))()
    Private Shared ReadOnly newListFactory As New Func(Of String, List(Of InteractionBase))(Function(s) New List(Of InteractionBase)())

    Public Sub New(removeInvalidItems As Boolean)
        Me.New(removeInvalidItems, Nothing, Nothing)
    End Sub

    Public Sub New(removeInvalidItems As Boolean, countCallback As Action(Of Integer), loadCallback As Action(Of PonyBase))
        Threading.Tasks.Parallel.Invoke(
            Sub() LoadPonyBases(removeInvalidItems, countCallback, loadCallback),
            AddressOf LoadInteractions,
            AddressOf LoadHouses)
    End Sub

    Private Sub LoadPonyBases(removeInvalidItems As Boolean, countCallback As Action(Of Integer), loadCallback As Action(Of PonyBase))
        Dim ponies As New Collections.Concurrent.ConcurrentBag(Of PonyBase)()
        Dim ponyBaseDirectories = Directory.GetDirectories(Path.Combine(EvilGlobals.InstallLocation, PonyBase.RootDirectory))
        If countCallback IsNot Nothing Then countCallback(ponyBaseDirectories.Length)
        Threading.Tasks.Parallel.ForEach(
            ponyBaseDirectories,
            Sub(folder)
                Dim pony = PonyBase.Load(Me, folder.Substring(folder.LastIndexOf(Path.DirectorySeparatorChar) + 1), removeInvalidItems)
                If pony IsNot Nothing Then
                    ponies.Add(pony)
                    If loadCallback IsNot Nothing Then loadCallback(pony)
                End If
            End Sub)
        Dim allBases = ponies.OrderBy(Function(pb) pb.Directory, StringComparer.OrdinalIgnoreCase).ToList()
        Dim randomIndex = allBases.FindIndex(Function(pb) pb.Directory = PonyBase.RandomDirectory)
        If randomIndex <> -1 Then
            _randomBase = allBases(randomIndex)
            allBases.RemoveAt(randomIndex)
        End If
        _bases = allBases.ToImmutableArray()
    End Sub

    Private Sub LoadInteractions()
        If Not File.Exists(Path.Combine(EvilGlobals.InstallLocation, PonyBase.RootDirectory, InteractionBase.ConfigFilename)) Then
            Options.PonyInteractionsExist = False
            Exit Sub
        End If
        Dim newListFactory = Function(s As String) New List(Of InteractionBase)()
        Using reader As New StreamReader(
            Path.Combine(EvilGlobals.InstallLocation, PonyBase.RootDirectory, InteractionBase.ConfigFilename))
            Do Until reader.EndOfStream
                Dim line = reader.ReadLine()

                ' Ignore blank lines, and those commented out with a single quote.
                If String.IsNullOrWhiteSpace(line) OrElse line(0) = "'" Then Continue Do

                Dim i As InteractionBase = Nothing
                If InteractionBase.TryLoad(line, i, Nothing) <> ParseResult.Failed Then
                    _interactions.GetOrAdd(i.InitiatorName, newListFactory).Add(i)
                End If
            Loop
        End Using
    End Sub

    Private Sub LoadHouses()
        ' TODO: Hook up to count and load callbacks.
        Dim houseDirectories = Directory.GetDirectories(Path.Combine(EvilGlobals.InstallLocation, HouseBase.RootDirectory))
        Dim houses As New Collections.Concurrent.ConcurrentBag(Of HouseBase)()
        Threading.Tasks.Parallel.ForEach(
            houseDirectories,
            Sub(folder)
                Try
                    Dim base = New HouseBase(folder)
                    houses.Add(base)
                Catch ex As Exception
                    ' Ignore errors from loading badly configured houses.
                End Try
            End Sub)
        _houses = houses.OrderBy(Function(hb) hb.Name).ToImmutableArray()
    End Sub

    ''' <summary>
    ''' Registers a change in directory name of a pony. Updates references accordingly.
    ''' </summary>
    ''' <param name="oldDirectory">The old directory name.</param>
    ''' <param name="newDirectory">The new directory name.</param>
    Public Sub ChangePonyDirectory(oldDirectory As String, newDirectory As String)
        If oldDirectory = newDirectory Then Return
        SyncLock _interactions
            If _interactions.ContainsKey(newDirectory) Then Throw New ArgumentException("The new directory already exists.", "newDirectory")
            If _interactions.ContainsKey(oldDirectory) Then
                Dim actions = _interactions(oldDirectory)
                _interactions.Remove(oldDirectory)
                For Each action In actions
                    action.InitiatorName = newDirectory
                Next
                _interactions(newDirectory) = actions
            End If
        End SyncLock
    End Sub

    ''' <summary>
    ''' Gets a list of interactions owned by the pony with the given directory identifier. This list may be edited.
    ''' </summary>
    ''' <param name="directory">The directory identifier of the pony.</param>
    ''' <returns>A list of all interactions where this pony is listed as the initiator.</returns>
    Public Function Interactions(directory As String) As List(Of InteractionBase)
        SyncLock _interactions
            Return _interactions.GetOrAdd(directory, newListFactory)
        End SyncLock
    End Function
End Class

Public NotInheritable Class PonyIniParser
    Private Sub New()
    End Sub

    Private Shared Function TryParse(Of T)(ByRef result As T, ByRef issues As ImmutableArray(Of ParseIssue),
                                                  parser As StringCollectionParser,
                                                  parse As Func(Of StringCollectionParser, T)) As ParseResult
        result = parse(parser)
        issues = parser.Issues.ToImmutableArray()
        Return parser.Result
    End Function

    Public Shared Function TryParseName(iniLine As String, directory As String, ByRef result As String, ByRef issues As ImmutableArray(Of ParseIssue)) As ParseResult
        Return TryParse(result, issues,
                                New StringCollectionParser(CommaSplitQuoteBraceQualified(iniLine), {"Identifier", "Name"}),
                                Function(p)
                                    p.NoParse()
                                    Return p.NoParse()
                                End Function)
    End Function

    Public Shared Function TryParseScale(iniLine As String, directory As String, ByRef result As Double, ByRef issues As ImmutableArray(Of ParseIssue)) As ParseResult
        Return TryParse(result, issues,
                                   New StringCollectionParser(CommaSplitQuoteBraceQualified(iniLine), {"Identifier", "Scale"}),
                                   Function(p)
                                       p.NoParse()
                                       Return p.ParseDouble(0, 0, 16)
                                   End Function)
    End Function

    Public Shared Function TryParseBehaviorGroup(iniLine As String, directory As String, ByRef result As BehaviorGroup, ByRef issues As ImmutableArray(Of ParseIssue)) As ParseResult
        Return TryParse(result, issues,
                                   New StringCollectionParser(CommaSplitQuoteBraceQualified(iniLine), {"Identifier", "Number", "Name"}),
                                   Function(p)
                                       p.NoParse()
                                       Dim bg As New BehaviorGroup(Nothing, 0)
                                       bg.Number = p.ParseInt32(0, 100)
                                       bg.Name = p.NotNullOrWhiteSpace(bg.Number.ToString())
                                       Return bg
                                   End Function)
    End Function
End Class

Public Delegate Function TryParse(Of T)(iniLine As String, directory As String,
                                        ByRef result As T, ByRef issues As ImmutableArray(Of ParseIssue)) As ParseResult

Public Delegate Function TryParseBase(Of T)(iniLine As String, directory As String, pony As PonyBase,
                                        ByRef result As T, ByRef issues As ImmutableArray(Of ParseIssue)) As ParseResult