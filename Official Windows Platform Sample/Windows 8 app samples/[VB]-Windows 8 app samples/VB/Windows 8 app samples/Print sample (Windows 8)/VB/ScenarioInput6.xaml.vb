﻿' THIS CODE AND INFORMATION IS PROVIDED "AS IS" WITHOUT WARRANTY OF
' ANY KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO
' THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
' PARTICULAR PURPOSE.
'
' Copyright (c) Microsoft Corporation. All rights reserved

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Windows.Foundation
Imports Windows.Foundation.Collections
Imports Windows.Graphics.Display
Imports Windows.Graphics.Imaging
Imports Windows.Graphics.Printing
Imports Windows.Graphics.Printing.OptionDetails
Imports Windows.Storage
Imports Windows.Storage.Streams
Imports Windows.UI
Imports Windows.UI.Text
Imports Windows.UI.Xaml
Imports Windows.UI.Xaml.Controls
Imports Windows.UI.Xaml.Controls.Primitives
Imports Windows.UI.Xaml.Data
Imports Windows.UI.Xaml.Input
Imports Windows.UI.Xaml.Media
Imports Windows.UI.Xaml.Media.Imaging
Imports Windows.UI.Xaml.Navigation
Imports Windows.UI.Xaml.Printing
Imports Windows.UI.Xaml.Shapes
Imports System.Runtime.InteropServices.WindowsRuntime

''' <summary>
''' Photo size options
''' </summary>
Friend Enum PhotoSize As Byte

    psFullPage
    ps4x6
    ps5x7
    ps8x10
End Enum


''' <summary>
''' Photo scaling options
''' </summary>
Friend Enum Scaling As Byte
    sShrinkToFit
    sCrop
End Enum

''' <summary>
''' Printable page description
''' </summary>
Friend class PageDescription

    Public Margin As Size

    Public PageSize As Size

    Public ViewablePageSize As Size

    Public PictureViewSize As Size

    Public IsContentCropped As Boolean

    Public Overloads Overrides Function Equals(other As Object) As Boolean

        Dim equal As Boolean = TypeOf other Is PageDescription

        Dim pageDescription As PageDescription = DirectCast(other, PageDescription)

        ' Detect if PageSize changed
        If equal Then
            equal = (Math.Abs(PageSize.Width - pageDescription.PageSize.Width) < Double.Epsilon) And
                    (Math.Abs(PageSize.Height - pageDescription.PageSize.Height) < Double.Epsilon)
        End If

        ' Detect if ViewablePageSize changed
        If equal Then
            equal = (Math.Abs(ViewablePageSize.Width - pageDescription.ViewablePageSize.Width) < Double.Epsilon) And
                    (Math.Abs(ViewablePageSize.Height - pageDescription.ViewablePageSize.Height) < Double.Epsilon)
        End If

        ' Detect if PictureViewSize changed
        If equal Then
            equal = (Math.Abs(PictureViewSize.Width - pageDescription.PictureViewSize.Width) < Double.Epsilon) And
                    (Math.Abs(PictureViewSize.Height - pageDescription.PictureViewSize.Height) < Double.Epsilon)
        End If

        ' Detect if cropping changed
        If equal Then
            equal = IsContentCropped = pageDescription.IsContentCropped
        End If

        Return equal
    End Function

End Class

''' <summary>
''' Photo printing scenario
''' </summary>
Partial Public NotInheritable Class ScenarioInput6
    Inherits BasePrintPage

#Region "Scenario specific constants."
    ''' <summary>
    ''' The app's number of photos
    ''' </summary>
    Private Const NumberOfPhotos As Integer = 9

    ''' <summary>
    ''' Constant for 96 DPI
    ''' </summary>
    Private Const DPI96 As Integer = 96
#End Region

    ''' <summary>
    ''' Current size settings for the image
    ''' </summary>
    Private photoSize As PhotoSize

    ''' <summary>
    ''' Current scale settings for the image
    ''' </summary>
    Private photoScale As Scaling

    ''' <summary>
    ''' A map of UIElements used to store the print preview pages.
    ''' </summary>
    Private pageCollection As Dictionary(Of Integer, UIElement) = New Dictionary(Of Integer, UIElement)

    ''' <summary>
    ''' Synchronization object used to sync access to pageCollection and the visual root(PrintingRoot).
    ''' </summary>
    Private printSync As Object = New Object()

    ''' <summary>
    ''' The current printer's page description used to create the content (size, margins, printable area)
    ''' </summary>
    Private currentPageDescription As PageDescription

    ''' <summary>
    ''' A request "number" used to describe a Paginate - GetPreviewPage session.
    ''' It is used by GetPreviewPage to determine, before calling SetPreviewPage, if the page content is out of date.
    ''' Flow:
    ''' Paginate will increment the request count and all subsequent GetPreviewPage calls will store a local copy and verify it before calling SetPreviewPage.
    ''' If another Paginate event is triggered while some GetPreviewPage workers are still executing asynchronously
    ''' their results will be discarded(ignored) because their request number is expired (the photo page description changed).
    ''' </summary>
    Private requestCount As Long


    Public Sub New()
        Me.InitializeComponent()

        photoSize = photoSize.psFullPage
        photoScale = Scaling.sShrinkToFit
    End Sub

    ''' <summary>
    ''' This is the event handler for PrintManager.PrintTaskRequested.
    ''' In order to ensure a good user experience, the system requires that the app handle the PrintTaskRequested event within the time specified 
    ''' by PrintTaskRequestedEventArgs->Request->Deadline.
    ''' Therefore, we use this handler to only create the print task.
    ''' The print settings customization can be done when the print document source is requested.
    ''' </summary>
    ''' <param name="sender">The print manager for which a print task request was made.</param>
    ''' <param name="e">The print taks request associated arguments.</param>
    Protected Overrides Sub PrintTaskRequested(sender As PrintManager, e As PrintTaskRequestedEventArgs)
        Dim printTask As PrintTask = Nothing
        printTask = e.Request.CreatePrintTask("VB Printing SDK Sample",
                                                Sub(sourceRequested)

                                                    Dim printDetailedOptions As PrintTaskOptionDetails = PrintTaskOptionDetails.GetFromPrintTaskOptions(printTask.Options)

                                                    ' Choose the printer options to be shown.
                                                    ' The order in which the options are appended determines the order in which they appear in the UI
                                                    printDetailedOptions.DisplayedOptions.Clear()
                                                    printDetailedOptions.DisplayedOptions.Add(Windows.Graphics.Printing.StandardPrintTaskOptions.MediaSize)
                                                    printDetailedOptions.DisplayedOptions.Add(Windows.Graphics.Printing.StandardPrintTaskOptions.Copies)

                                                    ' Create a new list option.
                                                    Dim photoSize As PrintCustomItemListOptionDetails = printDetailedOptions.CreateItemListOption("photoSize", "Photo Size")
                                                    photoSize.AddItem("psFullPage", "Full Page")
                                                    photoSize.AddItem("ps4x6", "4 x 6 in")
                                                    photoSize.AddItem("ps5x7", "5 x 7 in")
                                                    photoSize.AddItem("ps8x10", "8 x 10 in")

                                                    ' Add the custom option to the option list.
                                                    printDetailedOptions.DisplayedOptions.Add("photoSize")

                                                    Dim scaling As PrintCustomItemListOptionDetails = printDetailedOptions.CreateItemListOption("scaling", "Scaling")
                                                    scaling.AddItem("sShrinkToFit", "Shrink To Fit")
                                                    scaling.AddItem("sCrop", "Crop")

                                                    ' Add the custom option to the option list.
                                                    printDetailedOptions.DisplayedOptions.Add("scaling")

                                                    ' Set default orientation to landscape.
                                                    printTask.Options.Orientation = PrintOrientation.Landscape

                                                    ' Register for print task option changed notifications.
                                                    AddHandler printDetailedOptions.OptionChanged, AddressOf PrintDetailedOptionsOptionChanged

                                                    ''' <summary>
                                                    ''' Option change event handler
                                                    ''' </summary>
                                                    ''' <param name="s">The print task option details for which an option changed.</param>
                                                    ''' <param name="args">The event arguments containing the id of the changed option.</param>
                                                    AddHandler printTask.Completed,
                                                        Async Sub(s, args)
                                                            Await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                                                                                        Sub()
                                                                                            ClearPageCollection()

                                                                                            ' Reset image options to default values.
                                                                                            Me.photoScale = PrintSample.Scaling.sShrinkToFit
                                                                                            Me.photoSize = PrintSample.PhotoSize.psFullPage

                                                                                            ' Reset the current page description
                                                                                            currentPageDescription = Nothing

                                                                                            ' Notify the user when the print operation fails.
                                                                                            If args.Completion = PrintTaskCompletion.Failed Then
                                                                                                rootPage.NotifyUser("Failed to print.", NotifyType.ErrorMessage)
                                                                                            End If
                                                                                        End Sub)
                                                        End Sub

                                                    ' Set the document source.
                                                    sourceRequested.SetSource(printDocumentSource)
                                                End Sub)
    End Sub

    ''' <summary>
    ''' Option change event handler
    ''' </summary>
    ''' <param name="sender">The print task option details for which an option changed.</param>
    ''' <param name="args">The event arguments containing the id of the changed option.</param>
    Private Async Sub PrintDetailedOptionsOptionChanged(sender As PrintTaskOptionDetails, args As PrintTaskOptionChangedEventArgs)

        Dim invalidatePreview As Boolean = False

        ' For this scenario we are interested only when the 2 custom options change (photoSize & scaling) in order to trigger a preview refresh.
        ' Default options that change page aspect will trigger preview invalidation (refresh) automatically.
        ' It is safe to ignore verifying other options and(or) combinations here because during Paginate event(CreatePrintPreviewPages) we check if the PageDescription changed.
        If args.OptionId Is Nothing Then
            Return
        End If

        Dim optionId As String = args.OptionId.ToString()

        If optionId = "photoSize" Then

            Dim photoSizeOption As IPrintOptionDetails = sender.Options(optionId)
            Dim photoSizeValue As String = photoSizeOption.Value.ToString

            If Not String.IsNullOrEmpty(photoSizeValue) Then

                Select Case photoSizeValue
                    Case "psFullPage"
                        photoSize = photoSize.psFullPage
                    Case "ps4x6"
                        photoSize = photoSize.ps4x6
                    Case "ps5x7"
                        photoSize = photoSize.ps5x7
                    Case "ps8x10"
                        photoSize = photoSize.ps8x10
                End Select
                invalidatePreview = True
            End If
        End If

        If optionId = "scaling" Then

            Dim scalingOption As IPrintOptionDetails = sender.Options(optionId)
            Dim scalingValue As String = scalingOption.Value.ToString

            If Not String.IsNullOrEmpty(scalingValue) Then

                Select Case scalingValue

                    Case "sCrop"
                        photoScale = Scaling.sCrop
                    Case "sShrinkToFit"
                        photoScale = Scaling.sShrinkToFit

                End Select

                invalidatePreview = True
            End If
        End If

        ' Invalidate preview if one of the 2 options (photoSize, scaling) changed.
        If invalidatePreview Then
            Await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                                      Sub()
                                          printDocument.InvalidatePreview()
                                      End Sub)
        End If

    End Sub


    ''' <summary>
    ''' This is the event handler for Pagination.
    ''' </summary>
    ''' <param name="sender">The document for which pagination occurs.</param>
    ''' <param name="e">The pagination event arguments containing the print options.</param>
    Protected Overrides Sub CreatePrintPreviewPages(sender As Object, e As Windows.UI.Xaml.Printing.PaginateEventArgs)

        Dim printDoc As PrintDocument = sender

        ' A new "session" starts with each paginate event.
        Interlocked.Increment(requestCount)

        Dim pageDescription As PageDescription = New PageDescription()

        ' Get printer's page description.
        Dim printDetailedOptions As PrintTaskOptionDetails = PrintTaskOptionDetails.GetFromPrintTaskOptions(e.PrintTaskOptions)
        Dim printPageDescription As PrintPageDescription = e.PrintTaskOptions.GetPageDescription(0)

        ' Reset the error state
        printDetailedOptions.Options("photoSize").ErrorText = String.Empty

        ' Compute the printing page description (page size & center printable area)
        pageDescription.PageSize = printPageDescription.PageSize

        pageDescription.Margin.Width = Math.Max(printPageDescription.ImageableRect.Left,
                                                printPageDescription.ImageableRect.Right - printPageDescription.PageSize.Width)

        pageDescription.Margin.Height = Math.Max(printPageDescription.ImageableRect.Top,
                                                 printPageDescription.ImageableRect.Bottom - printPageDescription.PageSize.Height)

        pageDescription.ViewablePageSize.Width = printPageDescription.PageSize.Width - pageDescription.Margin.Width * 2
        pageDescription.ViewablePageSize.Height = printPageDescription.PageSize.Height - pageDescription.Margin.Height * 2

        ' Compute print photo area.
        Select Case photoSize

            Case photoSize.ps4x6
                pageDescription.PictureViewSize.Width = 4 * DPI96
                pageDescription.PictureViewSize.Height = 6 * DPI96
            Case photoSize.ps5x7
                pageDescription.PictureViewSize.Width = 5 * DPI96
                pageDescription.PictureViewSize.Height = 7 * DPI96
            Case photoSize.ps8x10
                pageDescription.PictureViewSize.Width = 8 * DPI96
                pageDescription.PictureViewSize.Height = 10 * DPI96
            Case photoSize.psFullPage
                pageDescription.PictureViewSize.Width = pageDescription.ViewablePageSize.Width
                pageDescription.PictureViewSize.Height = pageDescription.ViewablePageSize.Height
        End Select

        ' Try to maximize photo-size based on it's aspect-ratio
        If ((pageDescription.ViewablePageSize.Width > pageDescription.ViewablePageSize.Height) And (photoSize <> photoSize.psFullPage)) Then
            Dim swap = pageDescription.PictureViewSize.Width
            pageDescription.PictureViewSize.Width = pageDescription.PictureViewSize.Height
            pageDescription.PictureViewSize.Height = swap
        End If


        pageDescription.IsContentCropped = photoScale = Scaling.sCrop

        ' Recreate content only when : 
        ' - there is no current page description
        ' - the current page description doesn't match the new one
        If currentPageDescription Is Nothing Or
           (currentPageDescription IsNot Nothing AndAlso Not currentPageDescription.Equals(pageDescription)) Then

            ClearPageCollection()

            If pageDescription.PictureViewSize.Width > pageDescription.ViewablePageSize.Width Or
                pageDescription.PictureViewSize.Height > pageDescription.ViewablePageSize.Height Then

                printDetailedOptions.Options("photoSize").ErrorText = "Photo doesn’t fit on the selected paper"

                ' Inform preview that it has only 1 page to show.
                printDoc.SetPreviewPageCount(1, PreviewPageCountType.Intermediate)

                ' Add a custom "preview" unavailable page
                SyncLock printSync
                    pageCollection(0) = New PreviewUnavailable(pageDescription.PageSize, pageDescription.ViewablePageSize)
                End SyncLock
            Else

                ' Inform preview that is has #NumberOfPhotos pages to show.
                printDoc.SetPreviewPageCount(NumberOfPhotos, PreviewPageCountType.Intermediate)
            End If

            currentPageDescription = pageDescription
        End If
    End Sub


    ''' <summary>
    ''' This is the event handler for PrintDocument.GetPrintPreviewPage. It provides a specific print page preview,
    ''' in the form of an UIElement, to an instance of PrintDocument.
    ''' PrintDocument subsequently converts the UIElement into a page that the Windows print system can deal with.
    ''' </summary>
    ''' <param name="sender">The print documet.</param>
    ''' <param name="e">Arguments containing the requested page preview.</param>
    Protected Overrides Async Sub GetPrintPreviewPage(sender As Object, e As GetPreviewPageEventArgs)

        ' Store a local copy of the request count to use later to determine if the computed page is out of date.
        ' If the page preview is unavailable an async operation will generate the content.
        ' When the operation completes there is a chance that a pagination request was already made therefore making this page obsolete.
        ' If the page is obsolete throw away the result (don't call SetPreviewPage) since a new GetPrintPreviewPage will server that request.
        Dim requestNumber As Long = 0
        Interlocked.Exchange(requestNumber, requestCount)
        Dim pageNumber As Integer = e.PageNumber

        Dim page As UIElement = Nothing

        Dim pageReady As Boolean = False

        ' Try to get the page if it was previously generated.
        SyncLock printSync

            pageReady = pageCollection.TryGetValue(pageNumber - 1, page)
        End SyncLock

        If Not pageReady Then

            ' The page is not available yet.
            page = Await GeneratePage(pageNumber, currentPageDescription)

            ' If the ticket changed discard the result since the content is outdated.
            If Interlocked.CompareExchange(requestNumber, requestNumber, requestCount) <> requestCount Then
                Return
            End If

            ' Store the page in the list in case an invalidate happens but the content doesn't need to be regenerated.

            SyncLock printSync
                pageCollection(pageNumber - 1) = page

                ' Add the newly created page to the printing root which is part of the visual tree and force it to go
                ' through layout so that the linked containers correctly distribute the content inside them.
                printingRoot.Children.Add(page)
                printingRoot.InvalidateMeasure()
                printingRoot.UpdateLayout()
            End SyncLock
        End If

        Dim printDoc As PrintDocument = sender

        ' Send the page to preview.
        printDoc.SetPreviewPage(pageNumber, page)
    End Sub


    ''' <summary>
    ''' This is the event handler for PrintDocument.AddPages. It provides all pages to be printed, in the form of
    ''' UIElements, to an instance of PrintDocument. PrintDocument subsequently converts the UIElements
    ''' into a pages that the Windows print system can deal with.
    ''' </summary>
    ''' <param name="sender">The print document.</param>
    ''' <param name="e">Arguments containing the print task options.</param>
    Protected Overrides Async Sub AddPrintPages(sender As Object, e As AddPagesEventArgs)
        Dim printDoc As PrintDocument = sender

        ' Loop over all of the preview pages
        For i As Integer = 0 To NumberOfPhotos - 1 Step 1

            Dim page As UIElement = Nothing
            Dim pageReady As Boolean = False

            SyncLock printSync
                pageReady = pageCollection.TryGetValue(i, page)
            End SyncLock

            If Not pageReady Then

                ' If the page is not ready create a task that will generate its content.
                page = Await GeneratePage(i + 1, currentPageDescription)
            End If

            printDoc.AddPage(page)
        Next

        ' Indicate that all of the print pages have been provided.
        printDoc.AddPagesComplete()

        ' Reset the current page description as soon as possible since the PrintTask.Completed event might fire later (long running job)
        currentPageDescription = Nothing
    End Sub


    ''' <summary>
    ''' Helper function that clears the page collection and also the pages attached to the "visual root".
    ''' </summary>
    Private Sub ClearPageCollection()

        SyncLock printSync
            pageCollection.Clear()
            printingRoot.Children.Clear()
        End SyncLock

    End Sub

    ''' <summary>
    ''' Generic swap of 2 values
    ''' </summary>
    ''' <typeparam name="T">typename</typeparam>
    ''' <param name="v1">Value 1</param>
    ''' <param name="v2">Value 2</param>
    Private Sub Swap(Of T)(ByRef v1 As T, ByRef v2 As T)
        Dim Swap As T = v1
        v1 = v2
        v2 = Swap
    End Sub


    ''' <summary>
    ''' Generates a page containing a photo.
    ''' The image will be rotated if detected that there is a gain from that regarding size (try to maximize photo size).
    ''' </summary>
    ''' <param name="photoNumber">The photo number.</param>
    ''' <param name="pageDescription">The description of the printer page.</param>
    ''' <returns>A task that will return the page.</returns>
    Private Async Function GeneratePage(photoNumber As Integer, pageDescription As PageDescription) As Task(Of UIElement)
        Dim page As Canvas = New Canvas()
        page.Width = pageDescription.PageSize.Width
        page.Height = pageDescription.PageSize.Height


        Dim viewablePage As Canvas = New Canvas()
        viewablePage.Width = pageDescription.ViewablePageSize.Width
        viewablePage.Height = pageDescription.ViewablePageSize.Height

        viewablePage.SetValue(Canvas.LeftProperty, pageDescription.Margin.Width)
        viewablePage.SetValue(Canvas.TopProperty, pageDescription.Margin.Height)

        ' The image "frame" which also acts as a viewport
        Dim photoView As Grid = New Grid()
        photoView.Width = pageDescription.PictureViewSize.Width
        photoView.Height = pageDescription.PictureViewSize.Height


        ' Center the frame.
        photoView.SetValue(Canvas.LeftProperty, (viewablePage.Width - photoView.Width) / 2)
        photoView.SetValue(Canvas.TopProperty, (viewablePage.Height - photoView.Height) / 2)

        ' Return an async task that will complete when the image is fully loaded.
        Dim bitmap As WriteableBitmap = Await LoadBitmap(New Uri(String.Format("ms-appx:///Assets/photo{0}.jpg", photoNumber)),
                                                         pageDescription.PageSize.Width > pageDescription.PageSize.Height)
        If bitmap IsNot Nothing Then
            Dim image As Image = New Image
            image.Source = bitmap
            image.HorizontalAlignment = Windows.UI.Xaml.HorizontalAlignment.Center
            image.VerticalAlignment = Windows.UI.Xaml.VerticalAlignment.Center

            ' Use the real image size when croping or if the image is smaller then the target area (prevent a scale-up).
            If photoScale = Scaling.sCrop Or (bitmap.PixelWidth <= pageDescription.PictureViewSize.Width And
                                                bitmap.PixelHeight <= pageDescription.PictureViewSize.Height) Then
                image.Stretch = Stretch.None
                image.Width = bitmap.PixelWidth
                image.Height = bitmap.PixelHeight
            End If

            ' Add the newly created image to the visual root.
            photoView.Children.Add(image)
            viewablePage.Children.Add(photoView)
            page.Children.Add(viewablePage)
        End If

        ' Return the page with the image centered.
        Return page
    End Function

    ''' <summary>
    ''' Loads an image from an uri source and performs a rotation based on the print target aspect.
    ''' </summary>
    ''' <param name="source">The location of the image.</param>
    ''' <param name="landscape">A flag that indicates if the target (printer page) is in landscape mode.</param>
    ''' <returns>A task that will return the loaded bitmap.</returns>
    Private Async Function LoadBitmap(source As Uri, landscape As Boolean) As Task(Of WriteableBitmap)
        Dim file = Await StorageFile.GetFileFromApplicationUriAsync(source)
        Using stream = Await file.OpenAsync(FileAccessMode.Read)

            Dim decoder As BitmapDecoder = Await BitmapDecoder.CreateAsync(stream)

            Dim transform As BitmapTransform = New BitmapTransform()
            transform.Rotation = BitmapRotation.None
            Dim width As UInteger = decoder.PixelWidth
            Dim height As UInteger = decoder.PixelHeight

            If landscape And width < height Then
                transform.Rotation = BitmapRotation.Clockwise270Degrees
                Swap(width, height)
            ElseIf Not landscape And width > height Then
                transform.Rotation = BitmapRotation.Clockwise90Degrees
                Swap(width, height)
            End If

            Dim pixelData As PixelDataProvider = Await decoder.GetPixelDataAsync(decoder.BitmapPixelFormat,
                                                                          decoder.BitmapAlphaMode,
                                                                          transform,
                                                                          ExifOrientationMode.IgnoreExifOrientation,
                                                                          ColorManagementMode.DoNotColorManage)

            Dim bitmap As WriteableBitmap = New WriteableBitmap(width, height)
            Dim pixelBuffer = pixelData.DetachPixelData()
            Using pixelStream = bitmap.PixelBuffer.AsStream()
                pixelStream.Write(pixelBuffer, 0, pixelStream.Length)
            End Using

            Return bitmap
        End Using
    End Function
End Class
