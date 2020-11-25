In this project, we implemented Gossip and Push-Sum communication protocols using F# and Akka Actor Model on 2D Grid, Full, Line and Imperfect 2D network topologies.

How to run the program:
* Clone the code repository. Extract the zip file, go to extracted location from terminal and run following command:
dotnet fsi --langversion:preview project2.fsx numNodes topology algorithm


   * The “topology” parameter in the above command has one of the  values “full”,”2D”,”line”,”imp2D”.  


   * The “numNodes” parameter represents the number of actors involved.


   * “algorithm” parameter has one of “gossip” or “push-sum”values.


   * A sample command to run gossip algorithm for the “line” topology with 1000 actors:
dotnet fsi --langversion:preview project2.fsx 1000 line gossip
What is working: 
The Gossip and Push-Sum algorithms are working for all the topologies : Full Network, 2D Grid, Line, Imperfect 2D Grid. Also, we are getting convergence for both the algorithms (Gossip and Push Sum) for all the topologies.
Largest Network Used:


For the Gossip Algorithm:
      * Full Network Topology - 5000 nodes
      * 2D Grid - 5000 nodes
      * Line - 5000 nodes
      * Imperfect 2D - 5000 nodes
For the Push-Sum Algorithm:


      * Full Network Topology - 1000 nodes 
      * 2D Grid - 1000 nodes
      * Line - 1000 nodes
      * Imperfect 2D - 1000 nodes
