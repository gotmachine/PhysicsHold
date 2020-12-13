### Features
- Add a toolbar button for managing physics on landed vessels
- Option for disabling physics (joints/force/torque/velocities) interactions for selected vessels. This noticeably reduce lag with large bases and prevent physics kraken attacks.
- Option for partially re-enabling physics for robotic parts : allow stock robotics to work on "on hold" vessels
- Option for making the current "in physics" deformation permanent, stabilizing bases built on uneven terrain.
- "On-hold" vessels can still be docked to and undocked from, and Kerbals can still board and go to EVA from them
- KIS-adding parts works
- Attaching on-hold vessel with KAS connections **doesn't work** and **will result in the vessel destruction**

### Disclaimer

This is a proof-of-concept mod. As far as my tests have gone, it works reliably.
However, I don't intend to support it actively. It way break with future KSP versions, and it may stay unfixed if bugs are found.
Be aware that due to the hacky nature of this mod, it is likely to cause issues when used alongside other plugins.
I also don't intent to provide releases on the usual channels (KSP forums, CKAN, Spacedock, Curseforge...).
Feel free to fork and adopt this mod as your own if you want to make it available for the masses.

### Technical

This add a VesselModule active on all loaded vessels. It allow disabling physics for landed vessels. When the "physics hold" mode is enabled, all rigidbodies on the vessel are made kinematic by forcing the stock "packed" (or "on rails") state normally used during "physics easing" and non-physics timewarp.

When enabled, all rigibodies physics are disabled, making the vessel an unmovable object fixed at a given altitude/longitude/latitude. Non-kinematic vessels can still collide with it, but it will not react to collisions.

### Working and tested
- Docking : docking to a on hold vessel will seamlessly put the merged vessel on hold
- Undocking : if the initial vessel is on hold, both resulting vessels will be on hold after undocking
- Grabbing/ungrabbing (Klaw) has the same behavior as docking/undocking
- Decoupling : will insta-restore physics. Note that editor-docked docking ports are considered as decoupler
- Collisions will destroy on-hold parts if crash tolerance is exceeded (and trigger decoupling events)
- Breakable parts : solar panels, radiators and antennas will stay non-kinematic and can break.
- EVAing / boarding (hack in place to enable the kerbal portraits UI)
- Control input (rotation, translation, throttle...) is forced to zero by the stock vessel.packed check
- KIS attaching parts work as expected
- Stock robotics can be manually re-enabled on a "on hold" vessel. Enabling the option will restore physics to all child parts of a robotic part. Not that the option won't be available if the vessel root part is a child of a robotic part. Also, wheels can't be physics-enabled so any robotic part attempting to move a wheel won't be able to.

### Not working / Known issues

- Vessels using multi-node docking sometimes throw errors on undocking or when using the "make primary node" button. Not sure exactly what is going on, but the errors don't seem to cause major issues and messing around with the "make primary node" or in last resort reloading the scene seems to fix it.
- The stock "EVA ladder drift compensation" is disabled when the ladder is on a on-hold vessel
- KAS errors out as soon as a KAS connection exists on a on-hold vessel, resulting in vessels being immediately deleted. It probably can work at least partially since it is able to handle things in timewarp, but that would likely require quite a bit of extra handling on its side.
- ExtraPlanetaryLaunchpads seems to work if the vessel is on hold, but has an issue where the UI will stay stuck and never show the "Finalize" button when a vessel construction is being completed. Switching the scene will fix it.
   
### Untested / likely to have issues

- USI Konstruction things are reported to work, but I'm a bit skeptical and haven't done any test.
- Infernal Robotics : untested, probably won't work
  
### Licence

MIT
