﻿using BH.Engine.Reflection;
using BH.oM.Reflection;
using BH.Engine.UI;
using BH.oM.Reflection.Attributes;
using BH.oM.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BH.Engine.Serialiser;

namespace BH.UI.Templates
{
    public abstract class Caller
    {
        /*************************************/
        /**** Events                      ****/
        /*************************************/

        public event EventHandler<object> ItemSelected;


        /*************************************/
        /**** Properties                  ****/
        /*************************************/

        public virtual System.Drawing.Bitmap Icon_24x24 { get; protected set; }

        public virtual Guid Id { get; protected set; }

        public virtual string Name { get; protected set; } = "Undefined";

        public virtual string Category { get; protected set; } = "Undefined";

        public virtual string Description { get; protected set; } = "";

        public virtual int GroupIndex { get; protected set; } = 1;

        public virtual ISelector Selector { get; protected set; } = null;

        public DataAccessor DataAccessor { get; protected set; } = null;

        public List<ParamInfo> InputParams { get; protected set; } = new List<ParamInfo>();

        public List<ParamInfo> OutputParams { get; protected set; } = new List<ParamInfo>();


        /*************************************/
        /**** Constructors                ****/
        /*************************************/

        public Caller()
        {
            Engine.UI.Compute.LoadAssemblies();
        }


        /*************************************/
        /**** Public Methods              ****/
        /*************************************/

        public virtual bool Run()
        {
            BH.Engine.Reflection.Compute.ClearCurrentEvents();

            // Get all the inputs
            object[] inputs = new object[] { };
            try
            {
                inputs = m_CompiledGetters.Select(x => x(DataAccessor)).ToArray();
            }
            catch (Exception e)
            {
                RecordError(e, "This component failed to run properly. Inputs cannot be collected properly.\n");
                return false;
            }

            // Execute the method
            dynamic result = null;
            try
            {
                result = Run(inputs);
            }
            catch (Exception e)
            {
                RecordError(e, "This component failed to run properly. Are you sure you have the correct type of inputs?\n" +
                                 "Check their description for more details. Here is the error provided by the method:\n");
                return false;
            }

            // Set the output
            try
            {
                if (m_CompiledSetters.Count == 1)
                    m_CompiledSetters.First()(DataAccessor, result);
                else if (m_CompiledSetters.Count > 0)
                {
                    for (int i = 0; i < m_CompiledSetters.Count; i++)
                        m_CompiledSetters[i](DataAccessor, BH.Engine.Reflection.Query.Item(result as dynamic, i));
                }
            }
            catch (Exception e)
            {
                RecordError(e, "This component failed to run properly. Output data is calculated but cannot be set.\n");
                return false;
            }

            return true;
        }

        /*************************************/

        public abstract object Run(object[] inputs);

        /*************************************/

        public virtual bool SetItem(object item)
        {
            return true;
        }

        /*************************************/

        public virtual void SetDataAccessor(DataAccessor accessor)
        {
            DataAccessor = accessor;
            CompileInputGetters();
            CompileOutputSetters();
        }


        /*************************************/
        /**** Private Methods             ****/
        /*************************************/

        protected virtual void CompileInputGetters()
        {
            if (DataAccessor == null)
                return;

            m_CompiledGetters = new List<Func<DataAccessor, object>>();

            for (int index = 0; index < InputParams.Count; index++)
            {
                ParamInfo param = InputParams[index];
                UnderlyingType subType = param.DataType.UnderlyingType();
                string methodName = (subType.Depth == 0) ? "GetDataItem" : (subType.Depth == 1) ? "GetDataList" : "GetDataTree";
                MethodInfo method = DataAccessor.GetType().GetMethod(methodName).MakeGenericMethod(subType.Type);

                ParameterExpression lambdaInput1 = Expression.Parameter(typeof(DataAccessor), "accessor");
                ParameterExpression[] lambdaInputs = new ParameterExpression[] { lambdaInput1 };

                Expression[] methodInputs = new Expression[] { Expression.Constant(index) };
                MethodCallExpression methodExpression = Expression.Call(Expression.Convert(lambdaInput1, DataAccessor.GetType()), method, methodInputs);

                Func<DataAccessor, object> func = Expression.Lambda<Func<DataAccessor, object>>(Expression.Convert(methodExpression, typeof(object)), lambdaInputs).Compile();
                m_CompiledGetters.Add(func);
            }
        }

        /*************************************/

        protected virtual void CompileOutputSetters()
        {
            if (DataAccessor == null)
                return;

            m_CompiledSetters = new List<Func<DataAccessor, object, bool>>();

            for (int index = 0; index < OutputParams.Count; index++)
            {
                ParamInfo param = OutputParams[index];
                UnderlyingType subType = param.DataType.UnderlyingType();
                string methodName = (subType.Depth == 0) ? "SetDataItem" : (subType.Depth == 1) ? "SetDataList" : "SetDataTree";
                MethodInfo method = DataAccessor.GetType().GetMethod(methodName).MakeGenericMethod(subType.Type);

                ParameterExpression lambdaInput1 = Expression.Parameter(typeof(DataAccessor), "accessor");
                ParameterExpression lambdaInput2 = Expression.Parameter(typeof(object), "data");
                ParameterExpression[] lambdaInputs = new ParameterExpression[] { lambdaInput1, lambdaInput2 };

                Expression[] methodInputs = new Expression[] { Expression.Constant(index), Expression.Convert(lambdaInput2, method.GetParameters()[1].ParameterType) };
                MethodCallExpression methodExpression = Expression.Call(Expression.Convert(lambdaInput1, DataAccessor.GetType()), method, methodInputs);

                Func<DataAccessor, object, bool> function = Expression.Lambda<Func<DataAccessor, object, bool>>(methodExpression, lambdaInputs).Compile();
                m_CompiledSetters.Add(function);
            }
        }

        /*******************************************/

        protected static void RecordError(Exception e, string message = "")
        {
            if (e.InnerException != null)
                message += e.InnerException.Message;
            else
                message += e.Message;
            BH.Engine.Reflection.Compute.RecordError(message);
        }

        /*******************************************/

        protected void SetPossibleItems<T>(IEnumerable<T> items)
        {
            Selector = new Selector<T>(items, Name);
            Selector.ItemSelected += Selector_ItemSelected;
        }

        /*******************************************/

        protected void Selector_ItemSelected(object sender, object e)
        {
            SetItem(e);

            if (ItemSelected != null)
                ItemSelected(this, e);
        }


        /*************************************/
        /**** Private Fields              ****/
        /*************************************/

        protected List<Func<DataAccessor, object>> m_CompiledGetters = new List<Func<DataAccessor, object>>();
        protected List<Func<DataAccessor, object, bool>> m_CompiledSetters = new List<Func<DataAccessor, object, bool>>();

        /*************************************/
    }
}
