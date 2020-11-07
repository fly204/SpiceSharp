﻿using SpiceSharp.Behaviors;
using SpiceSharp.Entities;
using SpiceSharp.ParameterSets;
using SpiceSharp.Simulations;
using SpiceSharp.Validation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SpiceSharp.Components.Subcircuits;

namespace SpiceSharp.Components
{
    /// <summary>
    /// A subcircuit that can contain a collection of entities.
    /// </summary>
    /// <seealso cref="Entity" />
    /// <seealso cref="IComponent" />
    public class Subcircuit : Entity<SubcircuitBindingContext>,
        IComponent,
        IParameterized<Parameters>,
        IRuleSubject
    {
        private string[] _connections;

        /// <inheritdoc/>
        public Parameters Parameters { get; } = new Parameters();

        /// <inheritdoc/>
        public string Model { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<string> Nodes => new ReadOnlyCollection<string>(_connections);

        /// <summary>
        /// Gets the node map.
        /// </summary>
        /// <value>
        /// The node map.
        /// </value>
        /// <exception cref="NodeMismatchException">Thrown if the number of nodes don't match.</exception>
        private Bridge<string>[] NodeMap
        {
            get
            {
                if (Parameters.Definition == null)
                    return Array<Bridge<string>>.Empty();

                // Make a list of node bridges
                var pins = Parameters.Definition.Pins;
                var outNodes = _connections;
                if ((outNodes == null && pins.Count > 0) || outNodes.Length != pins.Count)
                    throw new NodeMismatchException(pins.Count, outNodes?.Length ?? 0);
                var nodes = new Bridge<string>[pins.Count];
                for (var i = 0; i < pins.Count; i++)
                    nodes[i] = new Bridge<string>(pins[i], outNodes[i]);
                return nodes;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Subcircuit"/> class.
        /// </summary>
        /// <param name="name">The name of the subcircuit.</param>
        /// <param name="definition">The subcircuit definition.</param>
        /// <param name="nodes">The nodes that the subcircuit is connected to.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="name"/> or <paramref name="definition"/> is <c>null</c>.</exception>
        public Subcircuit(string name, ISubcircuitDefinition definition, params string[] nodes)
            : base(name)
        {
            Parameters.Definition = definition.ThrowIfNull(nameof(definition));
            Connect(nodes);
        }

        /// <inheritdoc/>
        public override void CreateBehaviors(ISimulation simulation)
        {
            var behaviors = new BehaviorContainer(Name);
            if (Parameters.Definition != null && Parameters.Definition.Entities.Count > 0)
            {
                // Create our local simulation and binding context to allow our behaviors to do stuff
                var localSim = new SubcircuitSimulation(Name, simulation, Parameters.Definition, NodeMap);
                var context = new SubcircuitBindingContext(this, localSim, behaviors);
                Entities.DependencyInjection.DI.Resolve(simulation, this, behaviors, context);

                // Run the simulation
                localSim.Run(Parameters.Definition.Entities);

                // Allow the behaviors to fetch the behaviors if they want
                foreach (var behavior in behaviors)
                {
                    if (behavior is ISubcircuitBehavior subcktBehavior)
                        subcktBehavior.FetchBehaviors(context);
                }
            }
            simulation.EntityBehaviors.Add(behaviors);
        }

        /// <inheritdoc/>
        public IComponent Connect(params string[] nodes)
        {
            nodes.ThrowIfNull(nameof(nodes));
            _connections = new string[nodes.Length];
            for (var i = 0; i < nodes.Length; i++)
                _connections[i] = nodes[i].ThrowIfNull($"node {0}".FormatString(i + 1));
            return this;
        }

        /// <inheritdoc/>
        protected override void CopyFrom(ICloneable source)
        {
            base.CopyFrom(source);
            var s = (Subcircuit)source;
            _connections = new string[_connections.Length];
            for (var i = 0; i < _connections.Length; i++)
                _connections[i] = s._connections[i];
        }

        /// <inheritdoc/>
        public void Apply(IRules rules)
        {
            if (Parameters.Definition == null)
                return;

            var crp = rules.GetParameterSet<ComponentRuleParameters>();
            var newRules = new SubcircuitRules(rules, new ComponentRuleParameters(
                new VariableFactory(Name, crp.Factory, NodeMap, crp.Comparer),
                crp.Comparer));
            foreach (var c in Parameters.Definition.Entities)
            {
                if (c is IRuleSubject subject)
                    subject.Apply(newRules);
            }
        }
    }
}
