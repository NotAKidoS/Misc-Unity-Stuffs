Random assortment of stuff I have made in Unity which I don't version control elsewhere. Most of these are likely to be one-off experiments or attempts at making something which didn't fully work out for one reason or another.

Each thing will have a license associated with it in its source or folder.

### AnimationJobNetIk

Experiment with using an animation job to apply interpolated muscle values from the frame prior to remote avatars.

An interpolation step would run over the course of an entire frame and apply the next frame via animation job, solving a few issues like remote player positions being up-to-date at
beginning of frame instead of at end of frame.

### NAK.Contacts

Clean-room reimplementation of VRCs contacts system with added functionality which I had made canny posts for back when it initially released.

I have not actually tested if they replicate contacts as I have not touched the VRCSDK in years.

**Differences from VRC:**
- Can define a static value for Contant receiver type
- Added ProximityReceiverToSender and ProximityCenterToCenter receiver types
  - The original canny post has since been lost or deleted :(
  - [[Request] Option for proximity contacts to measure inverse to how they do now](<https://vrchat.canny.io/open-beta/p/request-option-for-proximity-contacts-to-measure-inverse-to-how-they-do-now>)
- Added CopyValueFromSender receiver type
- Added VelocityReceiver, VelocitySender, and VelocityMagnitude receiver types
- Does not have the quirk of receivers sticking to senders when disabled (VRLabs contact tracker tech)
  - I don't know how they did that tbh, was considering exposing as an actual feature here like a "lock"
- probably bunch more that i forgor

Note: You should make sure the system is hooked to run after constraint solving. I did not do that in my thing because I forgot until now.

### VisualClone

Unity VR avatar head hiding script. Meant to be as performant as possible without scaling bones (jank), swapping bones (alloc), forceRecalculationPerRender (does not scale), or a dedicated shadow clone.

Originally hosted on gist which was impossible to discover:

https://gist.github.com/NotAKidoS/662e3b0160461866b4b62eaf911b147a/

---

Here is the block of text where I tell you it's not my fault if you're bad at Unity.

> Use of these Unity Scripts is done so at the user's own risk and the creator cannot be held responsible for any issues arising from its use.
