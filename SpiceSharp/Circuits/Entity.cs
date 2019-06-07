﻿using System;
using System.Collections.Generic;
using System.Threading;
using SpiceSharp.Behaviors;
using SpiceSharp.Simulations;

namespace SpiceSharp.Circuits
{
    /// <summary>
    /// Base class for any circuit object that can take part in simulations.
    /// </summary>
    /// <remarks>
    /// Entities should not contain references to other entities, but only their name identifiers. In the methods 
    /// <see cref="SetupBehavior"/> and <see cref="BuildSetupDataProvider"/>  the entity should try to find the 
    /// necessary behaviors and parameters generated by other entities.
    /// </remarks>
    public abstract class Entity
    {
        private static Dictionary<Type, BehaviorFactoryDictionary> BehaviorFactories { get; } =
            new Dictionary<Type, BehaviorFactoryDictionary>();
        private static ReaderWriterLockSlim Lock { get; } = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

        /// <summary>
        /// Registers a behavior factory for an entity type.
        /// </summary>
        /// <param name="entityType">Type of the entity.</param>
        /// <param name="dictionary">The dictionary.</param>
        protected static void RegisterBehaviorFactory(Type entityType, BehaviorFactoryDictionary dictionary)
        {
            Lock.EnterWriteLock();
            try
            {
                BehaviorFactories.Add(entityType, dictionary);
            }
            finally
            {
                Lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Gets a collection of parameters.
        /// </summary>
        public ParameterSetDictionary ParameterSets { get; } = new ParameterSetDictionary();

        /// <summary>
        /// Gets the name of the entity.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Entity"/> class.
        /// </summary>
        /// <param name="name">The name of the entity.</param>
        protected Entity(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Sets a parameter with a specific name.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="name">The parameter name.</param>
        /// <param name="value">The parameter value.</param>
        /// <param name="comparer">The <see cref="IEqualityComparer{T}" /> implementation to use when comparing parameter names, or <c>null</c> to use the default <see cref="EqualityComparer{T}"/>.</param>
        /// <returns>False if the parameter could not be found.</returns>
        public void SetParameter(string name, double value, IEqualityComparer<string> comparer = null) => ParameterSets.SetParameter(name, value, comparer);

        /// <summary>
        /// Sets a parameter with a specific name.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        /// <param name="value">The parameter value.</param>
        /// <param name="comparer">The <see cref="IEqualityComparer{T}" /> implementation to use when comparing parameter names, or <c>null</c> to use the default <see cref="EqualityComparer{T}"/>.</param>
        /// <returns>False if the parameter could not be found.</returns>
        public void SetParameter<T>(string name, T value, IEqualityComparer<string> comparer = null) => ParameterSets.SetParameter(name, value, comparer);

        /// <summary>
        /// Creates behaviors of the specified type.
        /// </summary>
        /// <param name="type">The types of behaviors that the simulation wants, in the order that they will be called.</param>
        /// <param name="simulation">The simulation requesting the behaviors.</param>
        /// <param name="entities">The entities being processed, used by the entity to find linked entities.</param>
        /// <exception cref="ArgumentNullException">simulation</exception>
        public virtual void CreateBehaviors(Type[] types, Simulation simulation, EntityCollection entities)
        {            
            // Skip creating behaviors if the entity is already defined in the pool
            var pool = simulation.EntityBehaviors;
            if (pool.ContainsKey(Name))
                return;

            // Get the behavior factories for this entity
            BehaviorFactoryDictionary factories;
            Lock.EnterReadLock();
            try
            {
                if (!BehaviorFactories.TryGetValue(GetType(), out factories))
                    return;
            }
            finally
            {
                Lock.ExitReadLock();
            }

            // By default, go through the types in reverse order (to account for inheritance) and create
            // the behaviors
            EntityBehaviorDictionary ebd = null;
            var newBehaviors = new List<IBehavior>(types.Length);
            for (var i = types.Length - 1; i >= 0; i--)
            {
                // Skip creating behaviors that aren't needed
                if (ebd != null && ebd.ContainsKey(types[i]))
                    continue;
                Lock.EnterReadLock();
                try
                {
                    if (factories.TryGetValue(types[i], out var factory))
                    {
                        // Create the behavior
                        var behavior = factory(this);
                        pool.Add(behavior);
                        newBehaviors.Add(behavior);

                        // Get the dictionary if necessary
                        if (ebd == null)
                            ebd = pool[Name];
                    }
                }
                finally
                {
                    Lock.ExitReadLock();
                }
            }

            // Now set them up in the order they appear
            for (var i = newBehaviors.Count - 1; i >= 0; i--)
                SetupBehavior(newBehaviors[i], simulation);
        }
        
        /// <summary>
        /// Sets up the behavior.
        /// </summary>
        /// <param name="behavior">The behavior that needs to be set up.</param>
        /// <param name="simulation">The simulation.</param>
        /// <exception cref="ArgumentNullException">simulation</exception>
        protected virtual void SetupBehavior(IBehavior behavior, Simulation simulation)
        {
            if (simulation == null)
                throw new ArgumentNullException(nameof(simulation));

            // Build the setup behavior
            var provider = BuildSetupDataProvider(simulation.EntityParameters, simulation.EntityBehaviors);
            behavior.Setup(simulation, provider);
        }

        /// <summary>
        /// Build the data provider for setting up a behavior for the entity. The entity can control which parameters
        /// and behaviors are visible to behaviors using this method.
        /// </summary>
        /// <param name="parameters">The parameters in the simulation.</param>
        /// <param name="behaviors">The behaviors in the simulation.</param>
        /// <returns>A data provider for the behaviors.</returns>
        /// <exception cref="ArgumentNullException">
        /// parameters
        /// or
        /// behaviors
        /// </exception>
        protected virtual SetupDataProvider BuildSetupDataProvider(ParameterPool parameters, BehaviorPool behaviors)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));
            if (behaviors == null)
                throw new ArgumentNullException(nameof(behaviors));

            // By default, we include the parameters of this entity
            var result = new SetupDataProvider();
            result.Add("entity", parameters[Name]);
            result.Add("entity", behaviors[Name]);
            return result;
        }
    }
}
