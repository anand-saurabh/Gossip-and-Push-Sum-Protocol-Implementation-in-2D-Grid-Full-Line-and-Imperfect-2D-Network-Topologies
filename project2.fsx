#r "nuget: Akka.FSharp" 

open System
open System.Collections.Generic
open Akka.FSharp
open Akka.Actor

// Creating actor system
let actorSystem = System.create "actorSystem" (Configuration.defaultConfig())

// Creating random object to get random number in the code
let random: Random = Random()

let arg : string array = fsi.CommandLineArgs |> Array.tail

// If length smaller than 3 throw invalid arguments exception
if (arg.Length < 3) then
    invalidArg "command line arguments" "Please provide valid arguments while running this program" 

// Accept the arguemnts
let mutable numNodes = arg.[0] |> int
let topologyInputString = arg.[1] |> string
let algorithmInputString = arg.[2] |> string

// Supported toplogies
type TOPOLOGY = 
    | Line
    | Full
    | TwoD
    | TwoDImp

// Supplorted algorithm types
type ALGORITHM = 
    | Gossip
    | PushSum

// Assigning the enum value to topology
let topology: TOPOLOGY = 
    match topologyInputString with
        | "full" ->  TOPOLOGY.Full
        | "2D" -> TOPOLOGY.TwoD
        | "line" -> TOPOLOGY.Line
        | "imp2D" -> TOPOLOGY.TwoDImp
        | _ -> invalidArg "toplology" "argument must have values from set [full, 2D, line, imp2D]" 

// Assigning the enum value to algorithm
let algorithm: ALGORITHM =
    match algorithmInputString with
        | "gossip" -> ALGORITHM.Gossip
        | "push-sum" -> ALGORITHM.PushSum
        | _ -> invalidArg "algorithm" "argument must have values from set [gossip, push-sum]"


// Rounding up for getting squre grid in case of 2D and imp2D topology
// Calculating gridSize as well.
let mutable numRows : int = 0
let mutable numCols : int = 0
if (topology = TOPOLOGY.TwoD || topology = TOPOLOGY.TwoDImp) then
    let sqrtNumNodes = sqrt((double) numNodes)
    numRows <- (int) (ceil sqrtNumNodes)
    numCols <- (int) (ceil sqrtNumNodes)
    numNodes <- numRows * numCols
else
    numRows <- 1
    numCols <- numNodes

// Creating 2D array to store actors
let acotorsArray: IActorRef[,] = Array2D.create numRows numCols null

// Creating map to store status of the actor (i.e actor is converged or not)
let actorsStatusMap = new Dictionary<IActorRef, bool>()

// Supported message types for supervisor.
type SupervisorMessageTypes = 
    | Converged
    | Initialized

// Supported message types for gossip and pushsum actors
type ActorMessageType =
    | InIt of int * int
    | GossipCall
    | Active
    | PushSumCall of double * double

// Global variable to store number of actors initialized till now.
let mutable totalInitialized: int = 0

// Global variable to store number of actors converged till now.
let mutable totalConverged: int = 0

let supervisor (mailbox: Actor<_>) = 
    let rec loop() = actor {
        let! msg = mailbox.Receive()
        match msg with 
        | Initialized -> 
            totalInitialized <- totalInitialized + 1
        | Converged ->
            totalConverged <- totalConverged + 1
            // printfn "%d\n" totalConverged
        return! loop()
    }
    loop()
let supervisorRef = spawn actorSystem "supervisor" supervisor

let gossipConvergentActor (mailbox: Actor<_>) =
    let neighbors = new List<IActorRef>()

    let rec loop() = actor {
        let! msg = mailbox.Receive()
        match msg with 
        | InIt _ ->
            for i in 0 .. numRows - 1 do
                for j  in 0 .. numCols - 1 do
                    neighbors.Add acotorsArray.[i, j]
            mailbox.Self <! Active
        | Active ->
            if neighbors.Count > 0 then
                let randomNum = random.Next neighbors.Count 
                let randomActor: IActorRef = neighbors.[randomNum]

                // Check random actor's status
                let converged: bool = actorsStatusMap.[randomActor]

                // while random actor is already converged, pick next neighbor
                if (converged) then  
                    // Removing converged actor from current actor's neighbors list
                    neighbors.Remove randomActor
                else 
                    randomActor <! GossipCall

                mailbox.Self <! Active  

        return! loop()
    }
    loop()
    
// Gossip actor
let gossipActor (mailbox: Actor<_>) =
    let neighbors = new List<IActorRef>()
    let mutable recievedCount: int = 0

    let rec loop() = actor {
        // Getting next message from the queue.
        let! msg = mailbox.Receive()

        match msg with 
        // Init message to intialize the actor
        | InIt (i, j) ->
            match topology with
            | TOPOLOGY.Full ->
                for key in actorsStatusMap.Keys do
                    neighbors.Add key
                neighbors.Remove mailbox.Self
            
            | TOPOLOGY.TwoD
            | TOPOLOGY.TwoDImp ->
                let directions = [|
                    [|0; 1|]
                    [|1; 0|]
                    [|0; -1|]
                    [|-1; 0|]
                |]

                for dir in directions do
                    let newI = dir.[0] + i
                    let newJ = dir.[1] + j

                    if (newI >= 0 && newJ >=0 && newI < numRows && newJ < numCols) then
                        neighbors.Add acotorsArray.[newI, newJ]
                
                if topology = TOPOLOGY.TwoDImp then
                    let randomRowNum = random.Next numRows 
                    let randomColNum = random.Next numCols
                    neighbors.Add acotorsArray.[randomRowNum, randomColNum]
        
            | TOPOLOGY.Line ->
                let directions = [|
                    [|0; 1|]
                    [|0; -1|]
                |]
                for dir in directions do
                    let newI = dir.[0] + i
                    let newJ = dir.[1] + j

                    if (newI >= 0 && newJ >=0 && newI < numRows && newJ < numCols) then
                        neighbors.Add acotorsArray.[newI, newJ]

            supervisorRef <! SupervisorMessageTypes.Initialized

        // Handling inbound message
        | GossipCall ->
            // printfn "Call\n"
            // if current actor has recieved 10 gossip messages from others, this actor is converged
            if (recievedCount = 10) then  
                actorsStatusMap.[mailbox.Self] <- true
                supervisorRef <! Converged

            // if current actor has called the first time, make this actor active
            if (recievedCount = 0) then
                mailbox.Self <! Active

            recievedCount <- recievedCount + 1

        // If current actor is active keep on sending or gossping messages with neighbors
        | Active -> 
            if recievedCount < 11 then
                // Get a random neighbor actor
                let randomNum = random.Next neighbors.Count 
                let randomActor: IActorRef = neighbors.[randomNum]
                randomActor <! GossipCall
                mailbox.Self <! Active  
        

        return! loop()
    }
    loop()

let PushSumActor (mailbox: Actor<_>) = 
    let neighbors = new List<IActorRef>()
    let mutable s: double = 0.0
    let mutable w: double = 1.0
    let mutable count: int = 0
    let mutable alreadyConverged: bool= false

    let rec loop() = actor {
        // Getting next message from the queue.
        let! msg = mailbox.Receive()
        let sender = mailbox.Sender()
        match msg with 
        | InIt (i, j) ->
            s <- (double) (i * numRows + j)
            
            match topology with
            | TOPOLOGY.Full ->
                for key in actorsStatusMap.Keys do
                    neighbors.Add key
                neighbors.Remove mailbox.Self
            
            | TOPOLOGY.TwoD
            | TOPOLOGY.TwoDImp ->
                let directions = [|
                    [|0; 1|]
                    [|1; 0|]
                    [|0; -1|]
                    [|-1; 0|]
                |]

                for dir in directions do
                    let newI = dir.[0] + i
                    let newJ = dir.[1] + j

                    if (newI >= 0 && newJ >=0 && newI < numRows && newJ < numCols) then
                        neighbors.Add acotorsArray.[newI, newJ]
                
                if topology = TOPOLOGY.TwoDImp then
                    let randomRowNum = random.Next numRows 
                    let randomColNum = random.Next numCols
                    neighbors.Add acotorsArray.[randomRowNum, randomColNum]
        
            | TOPOLOGY.Line ->
                let directions = [|
                    [|0; 1|]
                    [|0; -1|]
                |]
                for dir in directions do
                    let newI = dir.[0] + i
                    let newJ = dir.[1] + j

                    if (newI >= 0 && newJ >=0 && newI < numRows && newJ < numCols) then
                        neighbors.Add acotorsArray.[newI, newJ]

            supervisorRef <! Initialized

        | PushSumCall (sc, wc) ->
           
            
            let ratio: double = (s + sc) / (w + wc)
            
            if (alreadyConverged) then
                let randomNum = random.Next neighbors.Count 
                let randomActor: IActorRef = neighbors.[randomNum]
                randomActor <! PushSumCall (sc, wc)

            else 
                if (abs(ratio - s / w) <= (10.0 ** -10.0)) then
                    count <- count + 1
                else 
                    count <- 0

                if (count = 3) then
                    // printfn "%A\n" (s / w)
                    supervisorRef <! Converged
                    alreadyConverged <- true
                
                s <- s + sc
                w <- w + wc

                s <- s/2.0
                w <- w/2.0

                let randomNum = random.Next neighbors.Count 
                let randomActor: IActorRef = neighbors.[randomNum]
                randomActor <! PushSumCall (s, w)

        return! loop()

    }
    loop()

match algorithm with
// Handling gossip
| Gossip ->
    for i in 0 .. numRows - 1 do
        for j  in 0 .. numCols - 1 do
            let key: string = "gossipActor" + "row" + (string) i + "col" + (string) j
            let actorRef = spawn actorSystem (key) gossipActor
            acotorsArray.[i,j] <- actorRef
            actorsStatusMap.Add (actorRef, false)

// Handling push sum
| PushSum ->
    for i in 0 .. numRows - 1 do
        for j  in 0 .. numCols - 1 do
            let key: string = "PushSumActor" + "row" + (string) i + "col" + (string) j
            let actorRef = spawn actorSystem (key) PushSumActor
            acotorsArray.[i,j] <- actorRef
            actorsStatusMap.Add (actorRef, false)

printfn "Initializing the actors...\n"

for i in 0 .. numRows - 1 do
        for j  in 0 .. numCols - 1 do
            acotorsArray.[i, j] <! InIt (i, j)
// Wait till all the actors are not initialized
while totalInitialized <> numNodes do
            ()
printfn "Successfully initialized the actors...\n"
        
// Get a random row number and column number
let randomRowNum = random.Next numRows 
let randomColNum = random.Next numCols

// Start the stopwatch to calculate the time to converge all actors
let stopWatch = System.Diagnostics.Stopwatch.StartNew()
// Start the gossip or push sum algorithm with a random actor        
match algorithm with
// Handling gossip
| Gossip ->
    acotorsArray.[randomRowNum, randomColNum] <! GossipCall
    let convergentActorRef = spawn actorSystem ("gossipConvergentActor") gossipConvergentActor
    convergentActorRef <! InIt (0,0)
| PushSum ->
    acotorsArray.[randomRowNum, randomColNum] <! PushSumCall (0.0, 0.0)

// Wait till all the actors are not converged
while totalConverged <> numNodes do
    ()

actorSystem.Terminate()
// Stop the stopwatch
stopWatch.Stop()

// Print the total time takes for convergence
printf "Total time to converge: %A\n\n" (stopWatch.Elapsed.TotalMilliseconds)

