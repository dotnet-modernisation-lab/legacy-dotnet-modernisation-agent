<%@ Page Language="C#" AutoEventWireup="false" CodeBehind="Orders.aspx.cs" Inherits="Shop.Web.OrderScreen" %>
<html><body><form runat="server">
<asp:Button ID="PlaceOrder" runat="server" Text="Place" OnClick="PlaceOrder_Click" OnClientClick="return confirm('sure');" />
<asp:Button ID="Cancel" runat="server" Text="Cancel" OnClick="CancelOrder" />
</form></body></html>
