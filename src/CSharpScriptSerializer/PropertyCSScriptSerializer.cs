using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpScriptSerialization
{
    public class PropertyCSScriptSerializer<T> : ConstructorCSScriptSerializer<T>
    {
        private readonly IReadOnlyCollection<PropertyData> _propertyData;

        public PropertyCSScriptSerializer()
            : this((Func<T, object>[])null)
        {
        }

        public PropertyCSScriptSerializer(IReadOnlyCollection<Func<T, object>> argumentGetters)
            : this(typeof(T).GetRuntimeProperties().Where(IsCandidateProperty), argumentGetters)
        {
        }

        public PropertyCSScriptSerializer(IEnumerable<PropertyInfo> properties)
            : this(properties, constructorParameterGetters: null)
        {
        }

        public PropertyCSScriptSerializer(IReadOnlyDictionary<string, Func<T, object>> propertyValueGetters)
            : this(propertyValueGetters, constructorParameterGetters: null)
        {
        }

        public PropertyCSScriptSerializer(IEnumerable<PropertyInfo> properties,
            IReadOnlyCollection<Func<T, object>> constructorParameterGetters)
            : this(properties.Select(p => new PropertyData(
                p.Name,
                p.PropertyType,
                CreatePropertyInitializer(p),
                o => GetDefault(p.PropertyType))).ToArray(),
                constructorParameterGetters)
        {
        }

        public PropertyCSScriptSerializer(IReadOnlyDictionary<string, Func<T, object>> propertyValueGetters,
            IReadOnlyCollection<Func<T, object>> constructorParameterGetters)
            : this(propertyValueGetters, constructorParameterGetters, new Dictionary<string, object>())
        {
        }

        public PropertyCSScriptSerializer(IReadOnlyDictionary<string, Func<T, object>> propertyValueGetters,
            IReadOnlyCollection<Func<T, object>> constructorParameterGetters,
            IReadOnlyDictionary<string, object> propertyDefaults)
            : this(
                propertyValueGetters,
                constructorParameterGetters,
                propertyDefaults.ToDictionary<KeyValuePair<string, object>, string, Func<T, object>>(
                    p => p.Key,
                    p => o => p.Value))
        {
        }

        // To not serialize properties give default that's always equal to the property value
        public PropertyCSScriptSerializer(IReadOnlyDictionary<string, Func<T, object>> propertyValueGetters,
            IReadOnlyCollection<Func<T, object>> constructorParameterGetters,
            IReadOnlyDictionary<string, Func<T, object>> propertyDefaultGetters)
            : this(constructorParameterGetters)
        {
            var referencedProperties = typeof(T).GetTypeInfo()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => propertyValueGetters.ContainsKey(p.Name));
            _propertyData = typeof(T).GetRuntimeProperties().Where(IsCandidateProperty)
                .Concat(referencedProperties).Distinct().Select(
                    p => new PropertyData(
                        p.Name,
                        p.PropertyType,
                        propertyValueGetters.GetValueOrDefault(p.Name, CreatePropertyInitializer(p)),
                        propertyDefaultGetters.GetValueOrDefault(p.Name, o => GetDefault(p.PropertyType))))
                .ToArray();
        }

        protected PropertyCSScriptSerializer(IReadOnlyCollection<PropertyData> propertyData,
            IReadOnlyCollection<Func<T, object>> constructorParameterGetters)
            : base(constructorParameterGetters)
        {
            _propertyData = propertyData;
        }

        protected override bool GenerateEmptyArgumentList => false;

        public override ExpressionSyntax GetCreation(object obj)
            => GetObjectCreationExpression((T)obj);

        protected override ObjectCreationExpressionSyntax GetObjectCreationExpression(T obj)
            => base.GetObjectCreationExpression(obj)
                .WithInitializer(AddNewLine(
                    SyntaxFactory.InitializerExpression(
                        SyntaxKind.ObjectInitializerExpression,
                        SyntaxFactory.SeparatedList<ExpressionSyntax>(
                            ToCommaSeparatedList(_propertyData
                                .Select(p => new
                                {
                                    p.PropertyName,
                                    p.PropertyType,
                                    PropertyValue = p.PropertyValueGetter(obj),
                                    PropertyDefault = p.PropertyDefaultGetter(obj)
                                })
                                .Where(p => !Equals(p.PropertyValue, p.PropertyDefault))
                                .Select(p => SyntaxFactory.AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    SyntaxFactory.IdentifierName(p.PropertyName),
                                    GetCreationExpression(p.PropertyValue))))))));

        protected static Func<T, object> CreatePropertyInitializer(PropertyInfo property)
        {
            var objectParameter = Expression.Parameter(typeof(T), name: "o");
            return Expression.Lambda<Func<T, object>>(
                Expression.Convert(
                    Expression.MakeMemberAccess(objectParameter, property),
                    typeof(object)),
                objectParameter)
                .Compile();
        }

        protected class PropertyData
        {
            public PropertyData(
                string propertyName,
                Type propertyType,
                Func<T, object> propertyValueGetter,
                Func<T, object> propertyDefaultGetter)
            {
                PropertyName = propertyName;
                PropertyType = propertyType;
                PropertyValueGetter = propertyValueGetter;
                PropertyDefaultGetter = propertyDefaultGetter;
            }

            public string PropertyName { get; }
            public Type PropertyType { get; }
            public Func<T, object> PropertyValueGetter { get; }
            public Func<T, object> PropertyDefaultGetter { get; }
        }
    }
}