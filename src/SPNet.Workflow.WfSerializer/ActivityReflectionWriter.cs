using System;
using System.Activities;
using System.Activities.Expressions;
using System.Linq;
using System.Reflection;

namespace SPNet.Workflow.WfSerializer
{
    internal static class ActivityReflectionWriter
    {
        public static object Create(Type type) => Activator.CreateInstance(type) ?? throw new InvalidOperationException("Could not instantiate " + type.FullName + ".");

        public static void SetProperty(object target, string propertyName, object value)
        {
            var property = target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => string.Equals(p.Name, propertyName, StringComparison.Ordinal) && p.CanWrite)
                .OrderByDescending(p => value == null || p.PropertyType.IsInstanceOfType(value))
                .FirstOrDefault();
            if (property == null || !property.CanWrite) throw new InvalidOperationException("Type " + target.GetType().FullName + " does not expose writable property " + propertyName + ".");
            property.SetValue(target, value, null);
        }

        public static void SetPropertyIfWritable(object target, string propertyName, object value)
        {
            var property = target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.Ordinal) && p.CanWrite);
            if (property != null && property.CanWrite) property.SetValue(target, value, null);
        }

        public static object GetProperty(object target, string propertyName, string missingMessage)
        {
            var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanRead) throw new InvalidOperationException(missingMessage);
            return property.GetValue(target, null) ?? throw new InvalidOperationException(missingMessage);
        }

        public static object CreateInArgument(Type resultType, object expressionActivity)
        {
            var argumentType = typeof(InArgument<>).MakeGenericType(resultType);
            var expressionType = typeof(Activity<>).MakeGenericType(resultType);
            return Activator.CreateInstance(argumentType, expressionType.IsInstanceOfType(expressionActivity) ? expressionActivity : throw new InvalidOperationException(expressionActivity.GetType().FullName + " is not an Activity<" + resultType.Name + ">."))!;
        }

        public static InArgument<string> CreateStringInArgumentFromActivity(object expressionActivity)
        {
            if (!(expressionActivity is Activity<string> stringActivity)) throw new InvalidOperationException(expressionActivity.GetType().FullName + " is not an Activity<String>.");
            return new InArgument<string>(stringActivity);
        }

        public static object CreateOutArgument(Type resultType, string variableName)
        {
            var argumentReferenceType = typeof(ArgumentReference<>).MakeGenericType(resultType);
            var argumentReference = Activator.CreateInstance(argumentReferenceType, variableName)!;
            var outArgumentType = typeof(OutArgument<>).MakeGenericType(resultType);
            return Activator.CreateInstance(outArgumentType, argumentReference)!;
        }

        public static object CreateInOutArgument(Type resultType, string variableName)
        {
            var argumentReferenceType = typeof(ArgumentReference<>).MakeGenericType(resultType);
            var argumentReference = Activator.CreateInstance(argumentReferenceType, variableName)!;
            var inOutArgumentType = typeof(InOutArgument<>).MakeGenericType(resultType);
            return Activator.CreateInstance(inOutArgumentType, argumentReference)!;
        }

        public static object CreateInArgumentReference(Type resultType, string variableName)
        {
            var argumentValueType = typeof(ArgumentValue<>).MakeGenericType(resultType);
            var argumentValue = Activator.CreateInstance(argumentValueType, variableName)!;
            return Activator.CreateInstance(typeof(InArgument<>).MakeGenericType(resultType), argumentValue)!;
        }

        public static void SetDynamicInArgumentReferenceIfWritable(object target, string propertyName, Type dynamicValueType, string argumentName)
        {
            var property = target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.Ordinal) && p.CanWrite);
            if (property == null || !property.CanWrite) return;
            property.SetValue(target, CreateInArgumentReference(dynamicValueType, argumentName), null);
        }
    }
}
