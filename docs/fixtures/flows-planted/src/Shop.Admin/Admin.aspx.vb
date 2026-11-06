Imports System.Web.UI
Imports Shop.Logic

Namespace Shop.Admin
    Public Class AdminScreen
        Inherits Page

        Private ReadOnly basket As New BasketService()

        Protected Sub StoreChanges(sender As Object, e As EventArgs) Handles SaveButton.Click
            basket.Clear()
        End Sub
    End Class
End Namespace
