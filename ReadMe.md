This add a VesselModule active on all loaded vessels. It allow disabling physics for landed vessels. 
When the "physics hold" mode is enabled, all rigidbodies on the vessel are made kinematic by forcing the stock 
"packed" (or "on rails") state normally used during "physics easing" and non-physics timewarp.

When enabled, all joint/force/torque physics are disabled, making the vessel an unmovable object fixed at
a given altitude/longitude/latitude. You can still collide with it, but it will not react to collisions.

The plugin adds a toolbar button opening a UI dialog listing all loaded vessels. From that dialog, the
user can select the on-hold state for each vessel and access a few other options.

#### Working and tested :
  - Docking : docking to a on hold vessel will seamlessly put the merged vessel on hold
  - Undocking : if the initial vessel is on hold, both resulting vessels will be on hold after undocking
  - Grabbing/ungrabbing (Klaw) has the same behavior as docking/undocking
  - Decoupling : will insta-restore physics. Note that editor-docked docking ports are considered as decoupler
  - Collisions will destroy on-hold parts if crash tolerance is exceeded (and trigger decoupling events)
  - Breakable parts : solar panels, radiators and antennas will stay non-kinematic and can break.
  - EVAing / boarding (hack in place to enable the kerbal portraits UI)
  - Control input (rotation, translation, throttle...) is forced to zero by the stock vessel.packed check
  - KIS attaching parts work as expected
  - Stock robotics can be manually re-enabled on a "on hold" vessel. Enabling the option will restore physics
    to all child parts of a robotic part. Not that the option won't be available if the vessel root part is
    a child of a robotic part. Also, wheels can't be physics-enabled so any robotic part attempting to move
    a wheel won't be able to.

#### Not working / Known issues :
  - Vessels using multi-node docking sometimes throw errors on undocking or when using the "make primary node"
    button. Not sure exactly what is going on, but the errors don't seem to cause major issues and messing
    around with the "make primary node" or in last resort reloading the scene seems to fix it.
  - The stock "EVA ladder drift compensation" is disabled when the ladder is on a on-hold vessel
  - Docking/undocking on a on-hold vessel will cause a very small displacement every time. This can 
    potentially become an issue over time. this said, the fix should be easy (save the original
    vessel latitude/longitude/altitude, and restore it after every undocking)
  - KAS errors out as soon as a KAS connection exists on a on-hold vessel, resulting in vessels being 
    immediately deleted. It probably can work at least partially since it is able to handle things in 
    timewarp, but that would likely require quite a bit of extra handling on its side.
   
#### Untested / likely to have issues :
  - USI Konstruction things are reported to work, but I'm a bit skeptical and haven't done any test.
  - Infernal Robotics : untested, probably won't work
  
#### Licence
MIT
