' ROTATE AND RESIZE WITH GDI+
' Andy De Filippo - 2017-12-30
' 
' Shows how to draw a shape-like editor (similar to the what's in Microsoft Office)
' implementing the use of anchors to resize and rotate a selection rectangle.
'
' Supports move, resize and rotate via mouse and, most important, shows how to handle
' the resize of an element that is already rotated, which is something I banged my head against
' for a few weeks before figuring out this solution.
'
' The code is released under the MIT License,
' see the LICENSE file in this repository.
' Mentioning of this article is appreciated.
'
' All code was written by me, except where indicated with a link to the source.

Imports System.Drawing.Drawing2D
Imports System.IO

Public Class FrmMain

#Region "Anti-flickering"

    ''' <summary>
    ''' Build form with the WS_EX_COMPOSITED and WS_EX_LAYERED styles to avoid flicekring
    ''' </summary>
    Protected Overrides ReadOnly Property CreateParams() As CreateParams
        Get
            Dim cp As CreateParams = MyBase.CreateParams
            ' WS_EX_COMPOSITED
            ' WS_EX_LAYERED
            cp.ExStyle = cp.ExStyle Or &H2000000
            cp.ExStyle = cp.ExStyle Or &H80000
            Return cp
        End Get
    End Property

#End Region

#Region "Form events"

    ''' <summary>
    ''' Clean up
    ''' </summary>
    Private Sub Form1_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing

        ' Dispose regions
        DisposeRegions()

        ' Dispose cursors
        For iCur As Integer = _oCollCursors.Count To 1 Step -1
            Dim oCur As Cursor = _oCollCursors.Item(iCur)
            _oCollCursors.Remove(iCur)
            oCur.Dispose()
            oCur = Nothing
        Next iCur

    End Sub

    ''' <summary>
    ''' Handle form paint event and redraw shapes
    ''' </summary>
    Private Sub Form1_Paint(sender As Object, e As PaintEventArgs) Handles Me.Paint
        Render()
    End Sub

#End Region

#Region "Mouse editing"

    ''' <summary>
    ''' Handle mouse down event and init resize, rotation or move
    ''' </summary>
    Private Sub Form1_MouseDown(sender As Object, e As MouseEventArgs) Handles Me.MouseDown
        Try

            ' Sanity checks
            If e.Button <> Windows.Forms.MouseButtons.Left Then Return
            If _eMouseOperation <> MouseOperation.None Then Return

            ' Reset mouse init point
            _ptMouseDown = PointF.Empty

            ' Check for anchor click
            For eAnchor As AnchorEnum = AnchorEnum.Nwse To AnchorEnum.Rotate
                If (_rgAnchors(eAnchor) IsNot Nothing) AndAlso (_rgAnchors(eAnchor).IsVisible(e.Location) = True) Then
                    ' The user clicekd on one of the anchors -> Initialize move/resize/rotation variables
                    _eMouseOperation = eAnchor
                    _ptMouseDown = e.Location
                    _rcMoveResizeInitRect = _rcRect
                    _ptResizeMouseDownCenterOrigin = _ptCenter
                    _snRotationInitAngle = _snAngle
                    Return
                End If
            Next eAnchor

            ' Not on an anchor -> Check for rectangle region
            If (_rgRect IsNot Nothing) AndAlso (_rgRect.IsVisible(e.Location) = True) Then
                ' Ensure the proper mouse cursor (Should be laready set by in the MouseMove event)
                Me.Cursor = MoveCursor
                ' Start a move operation
                _eMouseOperation = MouseOperation.Move
                _ptMouseDown = e.Location
                _rcMoveResizeInitRect = _rcRect
                Return
            End If

        Catch ex As Exception
            ' Error
            Debug.Print(ex.Message)
        End Try

    End Sub

    ''' <summary>
    ''' Handle mouse move in several context
    ''' </summary>
    Private Sub Form1_MouseMove(sender As Object, e As MouseEventArgs) Handles Me.MouseMove
        Try

            ' Check current operation
            Select Case _eMouseOperation
                Case MouseOperation.None

                    ' Init default cursor
                    Dim eCursor As Cursor = Cursors.Default

                    ' Check for anchor hovering
                    For eAnchor As AnchorEnum = AnchorEnum.Nwse To AnchorEnum.Rotate
                        If (_rgAnchors(eAnchor) IsNot Nothing) AndAlso (_rgAnchors(eAnchor).IsVisible(e.Location) = True) Then
                            ' The cursor is over one of the anchor regions -> Select the most appropriate cursor
                            eCursor = AnchorToCursor(eAnchor)
                            Exit For
                        End If
                    Next eAnchor

                    ' Not on an anchor -> Check for rectangle region
                    If eCursor = Cursors.Default Then
                        If (_rgRect IsNot Nothing) AndAlso _rgRect.IsVisible(e.Location) Then
                            ' The cursor is over the rectangle region -> Select the move cursor
                            eCursor = MoveCursor
                        End If
                    End If

                    ' Change cursor if needed
                    If Me.Cursor.Equals(eCursor) = False Then Me.Cursor = eCursor

                Case MouseOperation.Move

                    ' Calc movements by comparing MouseDown point and current cursor location
                    Dim iXMove As Integer = 0
                    If My.Computer.Keyboard.CtrlKeyDown = False Then
                        ' CTRL key is not pressed -> Allow horizontal movements
                        iXMove = (e.Location.X - _ptMouseDown.X)
                    End If
                    Dim iYMove As Integer = 0
                    If My.Computer.Keyboard.AltKeyDown = False Then
                        ' ALT key is not pressed -> Allow vertical movements
                        iYMove = (e.Location.Y - _ptMouseDown.Y)
                    End If
                    Dim tPt As New PointF(iXMove, iYMove)

                    ' Sanity check
                    If _rcMoveResizeInitRect.IsEmpty Then Return

                    ' Offset rectanlge stored at MouseClick by the amount calculated
                    Dim tRc As RectangleF = _rcMoveResizeInitRect
                    tRc.Offset(tPt)

                    ' Check for changes
                    If _rcRect.Equals(tRc) Then Return

                    ' Set new rectangle
                    _rcRect = tRc

                    ' Redraw
                    Render()

                Case MouseOperation.Nwse To MouseOperation.We

                    ' Get current mouse location
                    Dim tDest As PointF = e.Location

                    ' If there is a rotation angle, we need to rotate the current mouse location
                    ' around MouseDown point to calculate the movement as if the rectangle was not rotated
                    If _snAngle Then tDest = RotatePoint(tDest, _ptMouseDown, -_snAngle)

                    ' Calc movement
                    Dim tPt As New PointF((tDest.X - _ptMouseDown.X), (tDest.Y - _ptMouseDown.Y))

                    ' Get a copy of the initial rectangle
                    Dim tRc As RectangleF = _rcMoveResizeInitRect

                    ' Move according to anchor
                    Select Case _eMouseOperation
                        Case MouseOperation.Nwse
                            ' Left/Top
                            tRc.X += tPt.X
                            tRc.Width -= tPt.X
                            tRc.Y += tPt.Y
                            tRc.Height -= tPt.Y
                        Case MouseOperation.Ns
                            ' Top
                            tRc.Y += tPt.Y
                            tRc.Height -= tPt.Y
                        Case MouseOperation.Nesw
                            ' Right/Top
                            tRc.Width += tPt.X
                            tRc.Y += tPt.Y
                            tRc.Height -= tPt.Y
                        Case MouseOperation.Ew
                            ' Right
                            tRc.Width += tPt.X
                        Case MouseOperation.Senw
                            ' Right/Bottom
                            tRc.Width += tPt.X
                            tRc.Height += tPt.Y
                        Case MouseOperation.Sn
                            ' Bottom
                            tRc.Height += tPt.Y
                        Case MouseOperation.Swne
                            ' Left/Bottom
                            tRc.X += tPt.X
                            tRc.Width -= tPt.X
                            tRc.Height += tPt.Y
                        Case MouseOperation.We
                            ' Left
                            tRc.X += tPt.X
                            tRc.Width -= tPt.X
                    End Select

                    ' Check bounds
                    If tRc.Width < 1 Then Return
                    If tRc.Height < 1 Then Return

                    ' Check for changes
                    If _rcRect.Equals(tRc) Then Return

                    ' Set new rectangle
                    _rcRect = tRc

                    ' Redraw
                    Render()

                Case MouseOperation.Rotate
                    ' Rotate based on rectangle center point

                    ' Calc movement in real coordinates
                    Dim snAngle As Single = GetAngleBetweenTwoPointsWithFixedPoint(_ptMouseDown, e.Location, _ptResizeMouseDownCenterOrigin)
                    snAngle = -snAngle * 180.0# / Math.PI

                    ' Compute new rotation
                    _snAngle = EditRotateAngle(_snRotationInitAngle, snAngle)

                    ' Redraw
                    Render()

            End Select

        Catch ex As Exception
            ' Error
            Debug.Print(ex.Message)
        End Try

    End Sub


    ''' <summary>
    ''' Handle end of operations
    ''' </summary>
    Private Sub Form1_MouseUp(sender As Object, e As MouseEventArgs) Handles Me.MouseUp
        Try

            ' Check button
            If e.Button <> Windows.Forms.MouseButtons.Left Then Return
            If _eMouseOperation = MouseOperation.None Then Return

            ' If we are ending a resize operation on a rotated element, we need to ensure
            ' that the new rectangle is the one computed during the last render

            ' If _snAngle <> 0 Then -> 2021-12-09 - Thansk to CodeProject Member 12793961 for fixing this bug
            If (_snAngle <> 0) AndAlso (e.Location <> _ptMouseDown) Then
                Select Case _eMouseOperation
                    Case MouseOperation.Nwse To MouseOperation.We
                        _rcRect = _rcResizeMouseUpRenderRect
                End Select
            End If

            ' Reset current mouse operation
            _eMouseOperation = MouseOperation.None

            ' Redraw
            Render()

        Catch ex As Exception
            ' Error
            Debug.Print(ex.Message)
        End Try

    End Sub

#End Region

#Region "Rendering"

    ' Current location and size of rectangle
    Private _rcRect As RectangleF = New RectangleF(200, 100, 300, 180)

    ' Current rotation
    Private _snAngle As Single = 0.0

    ' Resize, move, rotate init variables
    Private _ptMouseDown As PointF
    Private _rcMoveResizeInitRect As RectangleF
    Private _ptResizeMouseDownCenterOrigin As PointF
    Private _snRotationInitAngle As Single
    Private _rcResizeMouseUpRenderRect As RectangleF

    ' Rotation init center point
    Private _ptCenter As PointF

    ' Anchors
    Private Enum AnchorEnum
        None
        Nwse
        Ns
        Nesw
        Ew
        Senw
        Sn
        Swne
        We
        Rotate
    End Enum

    ' Resize/rotate operations
    Private Enum MouseOperation
        None
        Nwse
        Ns
        Nesw
        Ew
        Senw
        Sn
        Swne
        We
        Rotate
        Move
    End Enum

    ' On-going mouse operation
    Private _eMouseOperation As MouseOperation = MouseOperation.None

    ' Anchor regions for hit testing
    Private _rgAnchors(AnchorEnum.Rotate) As Region ' Has extra Region, no big deal

    ' Rectangle region for hit testing
    Private _rgRect As Region

    ''' <summary>
    ''' Main rendering routine
    ''' </summary>
    Private Sub Render()

        ' Get surface Graphic
        Using oGfx As Graphics = Graphics.FromHwnd(Me.Handle)

            ' Sanity check
            If oGfx Is Nothing Then Exit Sub

            ' Clear background
            oGfx.Clear(Me.BackColor)

            ' Set quality
            oGfx.SmoothingMode = SmoothingMode.HighQuality

            ' Define center origin
            Dim ptCenter As PointF = New PointF(_rcRect.Left + (_rcRect.Width / 2), _rcRect.Top + (_rcRect.Height / 2))

            ' Check for on-going resize operation
            Select Case _eMouseOperation
                Case MouseOperation.Nwse To MouseOperation.We
                    ' Use last known center origin
                    ptCenter = _ptCenter
                Case Else
                    _ptCenter = ptCenter
            End Select

            ' Dispose previous regions
            DisposeRegions()

            ' Rotation
            Dim bRotated As Boolean = False
            Dim oMtx As Matrix = Nothing
            If (_snAngle > 0) Then

                ' Create rotation matrix
                oMtx = New Matrix
                oMtx.RotateAt(_snAngle, ptCenter, MatrixOrder.Append)
                oGfx.Transform = oMtx
                bRotated = True

            End If

            ' Store rectangle region
            _rgRect = New Region(_rcRect)
            If oMtx IsNot Nothing Then _rgRect.Transform(oMtx)

            ' Set rendering settings based on rotation
            Dim eSmothMode As SmoothingMode = oGfx.SmoothingMode
            Dim ePixOffset As PixelOffsetMode = oGfx.PixelOffsetMode
            Select Case _snAngle
                Case 0, 90, 180, 270
                    ' Reset cause lines render more precisely w/out HighQuality when there is no rotation
                    oGfx.SmoothingMode = SmoothingMode.AntiAlias
                    oGfx.PixelOffsetMode = PixelOffsetMode.Default
            End Select

            ' Draw backcolor
            oGfx.FillRectangle(Brushes.Azure, _rcRect)

            ' Draw frame
            oGfx.DrawRectangle(Pens.Red, _rcRect.Left, _rcRect.Top, _rcRect.Width, _rcRect.Height)

            ' Define anchor size
            Dim szAnhorSize As New Size(7, 7)

            ' Create anchor rectangles
            Dim rcAnchors(AnchorEnum.Rotate) As RectangleF
            rcAnchors(AnchorEnum.Nwse) = New RectangleF(_rcRect.Left - szAnhorSize.Width, _rcRect.Top - szAnhorSize.Height, szAnhorSize.Width, szAnhorSize.Height)
            Dim tRcNs As New RectangleF(((_rcRect.Left + (_rcRect.Width / 2)) - (szAnhorSize.Width / 2)), (_rcRect.Top - szAnhorSize.Height), szAnhorSize.Width, szAnhorSize.Height)
            If _rcRect.Width > (szAnhorSize.Width * 2) Then
                rcAnchors(AnchorEnum.Ns) = tRcNs
                rcAnchors(AnchorEnum.Sn) = New RectangleF(((_rcRect.Left + (_rcRect.Width / 2)) - (szAnhorSize.Width / 2)), _rcRect.Bottom, szAnhorSize.Width, szAnhorSize.Height)
            End If
            If _rcRect.Height > (szAnhorSize.Height * 2) Then
                rcAnchors(AnchorEnum.Ew) = New RectangleF(_rcRect.Right, ((_rcRect.Top + (_rcRect.Height / 2)) - (szAnhorSize.Height / 2)), szAnhorSize.Width, szAnhorSize.Height)
                rcAnchors(AnchorEnum.We) = New RectangleF((_rcRect.Left - szAnhorSize.Width), ((_rcRect.Top + (_rcRect.Height / 2)) - (szAnhorSize.Height / 2)), szAnhorSize.Width, szAnhorSize.Height)
            End If
            rcAnchors(AnchorEnum.Nesw) = New RectangleF(_rcRect.Right, (_rcRect.Top - szAnhorSize.Height), szAnhorSize.Width, szAnhorSize.Height)
            rcAnchors(AnchorEnum.Senw) = New RectangleF(_rcRect.Right, _rcRect.Bottom, szAnhorSize.Width, szAnhorSize.Height)
            rcAnchors(AnchorEnum.Swne) = New RectangleF((_rcRect.Left - szAnhorSize.Width), _rcRect.Bottom, szAnhorSize.Width, szAnhorSize.Height)
            With tRcNs
                rcAnchors(AnchorEnum.Rotate) = New RectangleF(.Left, .Top - (szAnhorSize.Height * 4) + 1, .Width + 1, .Height + 1)
            End With

            ' Create hit test regions
            For eAnchor As AnchorEnum = AnchorEnum.None To AnchorEnum.Rotate
                _rgAnchors(eAnchor) = New Region(rcAnchors(eAnchor))
            Next eAnchor

            ' Draw rotation anchor
            If _eMouseOperation = MouseOperation.Rotate Then
                oGfx.FillEllipse(Brushes.Yellow, rcAnchors(AnchorEnum.Rotate))
            Else
                oGfx.FillEllipse(Brushes.LightGreen, rcAnchors(AnchorEnum.Rotate))
            End If
            oGfx.DrawEllipse(Pens.Black, rcAnchors(AnchorEnum.Rotate))
            Dim pt1 As PointF = PointF.Empty
            Dim pt2 As PointF = PointF.Empty
            With rcAnchors(AnchorEnum.Rotate)
                pt1 = New PointF(.Left + (.Width / 2), .Bottom)
            End With
            With tRcNs
                pt2 = New PointF(.Left + (.Width / 2), .Bottom)
            End With
            oGfx.DrawLine(Pens.Red, pt1, pt2)

            ' Draw resize anchors
            For eAnchor As AnchorEnum = AnchorEnum.Nwse To AnchorEnum.We
                If eAnchor = _eMouseOperation Then
                    oGfx.FillRectangle(Brushes.Yellow, rcAnchors(eAnchor))
                Else
                    oGfx.FillRectangle(Brushes.WhiteSmoke, rcAnchors(eAnchor))
                End If
                oGfx.DrawRectangle(Pens.Black, rcAnchors(eAnchor).Left, rcAnchors(eAnchor).Top, rcAnchors(eAnchor).Width, rcAnchors(eAnchor).Height)
            Next eAnchor

            ' Reset transform
            If bRotated Then
                bRotated = False
                oGfx.ResetTransform()
            End If

            ' Restore settings
            oGfx.SmoothingMode = eSmothMode
            oGfx.PixelOffsetMode = ePixOffset

            ' Apply transformation on anchor regions for hit-testing
            If oMtx IsNot Nothing Then
                For Each oRgn As Region In _rgAnchors
                    oRgn.Transform(oMtx)
                Next oRgn
            End If

            ' Check for rotation
            If (_rgRect IsNot Nothing) AndAlso (_snAngle <> 0.0) Then

                ' Get rotatetd rectangle bounds
                Dim rfNewBounds As RectangleF = _rgRect.GetBounds(oGfx)

                ' Check for resize operation on a rotated element
                If (_eMouseOperation >= MouseOperation.Nwse) AndAlso _eMouseOperation <= MouseOperation.We Then

                    ' Get center origin of the region
                    Dim ptNewScreenCenterOrigin As New PointF(rfNewBounds.Left + (rfNewBounds.Width / 2), rfNewBounds.Top + (rfNewBounds.Height / 2))

                    ' Compute a rectangle based on source rectangle size and located around the center point
                    ' of the bounds of the rotated region
                    Dim rcNewRenderRect As New RectangleF((ptNewScreenCenterOrigin.X - (_rcRect.Width / 2)), _
                                                          (ptNewScreenCenterOrigin.Y - (_rcRect.Height / 2)), _
                                                          _rcRect.Width, _
                                                          _rcRect.Height)

                    ' Store for mosue up
                    _rcResizeMouseUpRenderRect = rcNewRenderRect

                End If

            End If

            ' Clean up
            If bRotated Then oGfx.ResetTransform()
            If oMtx IsNot Nothing Then
                oMtx.Dispose()
                oMtx = Nothing
            End If

        End Using

    End Sub

#End Region

#Region "Helper methods and properties"

    ''' <summary>
    ''' Dispose regions prior to re-drawing and form closing to avoid memory leaks
    ''' </summary>
    Private Sub DisposeRegions()

        ' Dispose anchor regions
        For eAnchor As AnchorEnum = AnchorEnum.None To AnchorEnum.Rotate
            If _rgAnchors(eAnchor) IsNot Nothing Then
                _rgAnchors(eAnchor).Dispose()
                _rgAnchors(eAnchor) = Nothing
            End If
        Next eAnchor

        ' Dispose rectangle region
        If _rgRect IsNot Nothing Then
            _rgRect.Dispose()
            _rgRect = Nothing
        End If

    End Sub

    ' List of in-memory cursors
    Private _oCollCursors As New Collection

    ''' <summary>
    ''' Return most adapt cursor based on rotation
    ''' </summary>
    ''' <remarks>
    ''' It's best to use standard cursors even if they don't precisely match the current rotation.
    ''' Creating and rotating own arrow cursors will result in poor rendering.
    ''' Try with a rotate shape in Microsoft office.
    ''' </remarks>
    Private Function AnchorToCursor(ByVal eAnchor As AnchorEnum) As Cursor

        ' Internal angle variable
        Dim snAngle As Single = _snAngle

        Select Case eAnchor

            Case AnchorEnum.Rotate

                ' Get rotate cursor
                Return RotateCursor

            Case Else ' AnchorEnum.Ew, AnchorEnum.We

                ' Define snAngle
                Select Case eAnchor
                    Case AnchorEnum.Nwse, AnchorEnum.Senw
                        snAngle += 45
                    Case AnchorEnum.Ns, AnchorEnum.Sn
                        snAngle += 90
                    Case AnchorEnum.Nesw, AnchorEnum.Swne
                        snAngle += 135
                    Case Else ' AnchorEnum.Ew, AnchorEnum.We
                        ' No additional rotation
                End Select
                If snAngle > 360 Then snAngle -= 360

                ' Select base on snAngle
                Select Case CInt(snAngle)
                    Case 26 To 68, 204 To 248
                        Return Cursors.SizeNWSE
                    Case 69 To 113, 249 To 293
                        Return Cursors.SizeNS
                    Case 114 To 158, 294 To 338
                        Return Cursors.SizeNESW
                    Case Else ' 0 To 23, 159 To 203, 339 To 360
                        Return Cursors.SizeWE
                End Select

        End Select

    End Function

    ''' <summary>
    ''' Return a "rotate" custom cursor
    ''' </summary>
    Private ReadOnly Property RotateCursor As Cursor
        Get

            ' Check cache
            If _oCollCursors.Contains("Rotate") Then
                Return _oCollCursors("Rotate")
            End If

            ' Create and save in cache for future uses
            Using oMs As New MemoryStream(My.Resources.rotate)
                oMs.Position = 0
                Dim oCur As New Cursor(oMs)
                _oCollCursors.Add(oCur, "Rotate")
                Return oCur
            End Using

        End Get
    End Property

    ''' <summary>
    ''' Create a "move" cursor
    ''' </summary>
    Private ReadOnly Property MoveCursor As Cursor
        Get

            ' Check cache
            If _oCollCursors.Contains("SizeAll") Then
                Return _oCollCursors("SizeAll")
            End If

            ' Create and save in cache for future uses
            Using oMs As New MemoryStream()
                My.Resources.hand_open.Save(oMs)
                oMs.Position = 0
                Dim oCur As New Cursor(oMs)
                _oCollCursors.Add(oCur, "SizeAll")
                Return oCur
            End Using

        End Get
    End Property

    ''' <summary>
    ''' Rotate angle based on mouse movement
    ''' </summary>
    Private Function EditRotateAngle(snRotation As Single, dbAngle As Double) As Single

        ' Get new angle and trim decimals
        Dim snOut As Single = Int(snRotation + dbAngle)

        ' Keep within 0 ~ 359.9
        If (snOut >= 360) Then snOut = snOut Mod 360
        If (snOut < 0) Then snOut = 360 - (-snOut Mod 360)

        ' Quantize
        If My.Computer.Keyboard.AltKeyDown = False Then
            ' Quantize when ALT is not pressed
            Dim bQuantized As Boolean = False
            For snTarget As Single = 0.0 To 360 Step 45
                snOut = QuantizeRotation(snOut, snTarget, bQuantized)
                If bQuantized Then Exit For
            Next snTarget
        Else
            ' Free rotation when ALT is NOT pressed
            snRotation = snOut
        End If

        ' Return
        Return snOut

    End Function

    ''' <summary>
    ''' Quantize an angle if its within +/- a quantization interval 
    ''' </summary>
    Private Function QuantizeRotation(ByVal snRotation As Single, _
                                      ByVal snTarget As Single, _
                                      ByRef bQuantized As Boolean) As Single

        ' Quantize angle
        Dim snQuantize As Single = 6

        ' Set init
        Dim snLowRef As Single = (snTarget - snQuantize)
        Dim snHiRef As Single = (snTarget + snQuantize)

        ' Keep targets within boundires
        If (snLowRef >= 360) Then snLowRef = snLowRef Mod 360
        If (snLowRef < 0) Then snLowRef = 360 - (-snLowRef Mod 360)
        If (snHiRef >= 360) Then snHiRef = snHiRef Mod 360
        If (snHiRef < 0) Then snHiRef = 360 - (-snHiRef Mod 360)

        If snLowRef < snHiRef Then
            Select Case snRotation
                Case snLowRef To snHiRef
                    ' Quantized
                    bQuantized = True
                    Return snTarget
                Case Else
                    ' No quantize
                    Return snRotation
            End Select
        Else
            Select Case snRotation
                Case snLowRef To 360, 0 To snHiRef
                    ' Quantized
                    bQuantized = True
                    Return snTarget
                Case Else
                    ' No quantize
                    Return snRotation
            End Select
        End If
    End Function

    ''' <summary>
    ''' Roate a point around another point
    ''' </summary>
    ''' <remarks>http://stackoverflow.com/questions/13695317/rotate-a-point-around-another-point</remarks>
    Private Function RotatePoint(pointToRotate As PointF, centerPoint As PointF, angleInDegrees As Double) As PointF
        Dim angleInRadians As Double = angleInDegrees * (Math.PI / 180)
        Dim cosTheta As Double = Math.Cos(angleInRadians)
        Dim sinTheta As Double = Math.Sin(angleInRadians)
        Return New PointF() With {.X = CInt(cosTheta * (pointToRotate.X - centerPoint.X) - sinTheta * (pointToRotate.Y - centerPoint.Y) + centerPoint.X), _
                                  .Y = CInt(sinTheta * (pointToRotate.X - centerPoint.X) + cosTheta * (pointToRotate.Y - centerPoint.Y) + centerPoint.Y)}
    End Function

    ''' <summary>
    ''' Calculate the angle between two points
    ''' </summary>
    ''' <remarks>
    ''' http://stackoverflow.com/questions/26076656/calculating-angle-between-two-points-java
    ''' </remarks>
    Private Function GetAngleBetweenTwoPointsWithFixedPoint(tPt1 As PointF, tPt2 As PointF, tPtFixed As PointF) As Single
        Dim snAngle1 As Single = Math.Atan2(tPt1.Y - tPtFixed.Y, tPt1.X - tPtFixed.X)
        Dim snAngle2 As Single = Math.Atan2(tPt2.Y - tPtFixed.Y, tPt2.X - tPtFixed.X)
        Return snAngle1 - snAngle2
    End Function

#End Region

End Class
