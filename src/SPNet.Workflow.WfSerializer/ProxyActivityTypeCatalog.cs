using System;
using System.Collections.Generic;
using System.Reflection;

namespace SPNet.Workflow.WfSerializer
{
    internal sealed class ProxyActivityTypeCatalog
    {
        public ProxyActivityTypeCatalog(Assembly sharePointAssembly, Assembly microsoftActivitiesAssembly)
        {
            Calc = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.Calc");
            WriteToHistory = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.WriteToHistory");
            SetWorkflowStatus = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.SetWorkflowStatus");
            Comment = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.Comment");
            DelayFor = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.DelayFor");
            DelayUntil = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.DelayUntil");
            LookupWorkflowContextProperty = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.LookupWorkflowContextProperty");
            GetCurrentListId = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.GetCurrentListId");
            GetCurrentItemGuid = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.GetCurrentItemGuid");
            SetField = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.SetField");
            CreateListItem = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.CreateListItem");
            UpdateListItem = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.UpdateListItem");
            DeleteListItem = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.DeleteListItem");
            LookupSPListItemStringProperty = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.LookupSPListItemStringProperty");
            LookupSPListItemIntProperty = GetOptionalType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.LookupSPListItemIntProperty");
            CallHTTPWebService = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.CallHTTPWebService");
            Email = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.Email");
            ExpandInitFormUsers = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.ExpandInitFormUsers");
            SingleTask = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.SingleTask");
            LookupSPListItemPropertyNameInREST = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.LookupSPListItemPropertyNameInREST");
            GetDynamicValueProperty = GetRequiredType(microsoftActivitiesAssembly, "Microsoft.Activities.GetDynamicValueProperty`1").MakeGenericType(typeof(string));
            ToStringExpression = GetRequiredType(microsoftActivitiesAssembly, "Microsoft.Activities.Expressions.ToString");
            DynamicValue = GetRequiredType(microsoftActivitiesAssembly, "Microsoft.Activities.DynamicValue");
            BuildDictionary = GetRequiredType(microsoftActivitiesAssembly, "Microsoft.Activities.BuildDictionary`2").MakeGenericType(typeof(string), typeof(object));
            ComparisonExpressionTypes = new WfActivityBuilderSerializer.ComparisonExpressionTypes(microsoftActivitiesAssembly);
        }

        public Type Calc { get; }
        public Type WriteToHistory { get; }
        public Type SetWorkflowStatus { get; }
        public Type Comment { get; }
        public Type DelayFor { get; }
        public Type DelayUntil { get; }
        public Type LookupWorkflowContextProperty { get; }
        public Type GetCurrentListId { get; }
        public Type GetCurrentItemGuid { get; }
        public Type SetField { get; }
        public Type CreateListItem { get; }
        public Type UpdateListItem { get; }
        public Type DeleteListItem { get; }
        public Type LookupSPListItemStringProperty { get; }
        public Type? LookupSPListItemIntProperty { get; }
        public Type CallHTTPWebService { get; }
        public Type Email { get; }
        public Type ExpandInitFormUsers { get; }
        public Type SingleTask { get; }
        public Type LookupSPListItemPropertyNameInREST { get; }
        public Type GetDynamicValueProperty { get; }
        public Type ToStringExpression { get; }
        public Type DynamicValue { get; }
        public Type BuildDictionary { get; }
        public WfActivityBuilderSerializer.ComparisonExpressionTypes ComparisonExpressionTypes { get; }

        public WfActivityBuilderSerializer.ValueExpressionTypes CreateValueExpressionTypes() =>
            new WfActivityBuilderSerializer.ValueExpressionTypes(ToStringExpression, LookupWorkflowContextProperty, GetCurrentListId, GetCurrentItemGuid, LookupSPListItemStringProperty, BuildDictionary);

        public Dictionary<string, Type> CreateBuildContextTypes()
        {
            var types = new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                ["Calc"] = Calc,
                ["WriteToHistory"] = WriteToHistory,
                ["SetWorkflowStatus"] = SetWorkflowStatus,
                ["Comment"] = Comment,
                ["DelayFor"] = DelayFor,
                ["DelayUntil"] = DelayUntil,
                ["LookupWorkflowContextProperty"] = LookupWorkflowContextProperty,
                ["GetCurrentListId"] = GetCurrentListId,
                ["GetCurrentItemGuid"] = GetCurrentItemGuid,
                ["SetField"] = SetField,
                ["CreateListItem"] = CreateListItem,
                ["UpdateListItem"] = UpdateListItem,
                ["DeleteListItem"] = DeleteListItem,
                ["LookupSPListItemStringProperty"] = LookupSPListItemStringProperty,
                ["CallHTTPWebService"] = CallHTTPWebService,
                ["Email"] = Email,
                ["ExpandInitFormUsers"] = ExpandInitFormUsers,
                ["SingleTask"] = SingleTask,
                ["LookupSPListItemPropertyNameInREST"] = LookupSPListItemPropertyNameInREST,
                ["GetDynamicValueProperty"] = GetDynamicValueProperty
            };
            if (LookupSPListItemIntProperty != null) types["LookupSPListItemIntProperty"] = LookupSPListItemIntProperty;
            return types;
        }

        private static Type GetRequiredType(Assembly assembly, string typeName) =>
            assembly.GetType(typeName, throwOnError: true, ignoreCase: false);

        private static Type? GetOptionalType(Assembly assembly, string typeName) =>
            assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
    }
}
